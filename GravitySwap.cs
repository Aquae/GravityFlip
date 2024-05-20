using log4net;
using Microsoft.Xna.Framework;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace GravitySwap
{
    public class AlignedPlayer : ModPlayer
    {
        Axiom config = ModContent.GetInstance<Axiom>();
        public ILog Logger = ModContent.GetInstance<GravityLink>().Logger;

        private int _partnerID = -1;
        public int PartnerID
        {
            get { return _partnerID; }
            set { _partnerID = value; }
        }
        public bool IsFlipped = true;
        public bool IsEntangled => PartnerID != -1 && (Main.player[PartnerID]?.active ?? false);
        private bool JustPressedUp = false;

        public override void OnEnterWorld()
        {
            Terraria.Chat.ChatHelper.SendChatMessageToClient(
                NetworkText.FromLiteral($"[c/{config.NoticeColor}:Your mass is undergoing quantum alignment...]"),
                Color.White,
                Player.whoAmI
                );
            
            EmitRizz();
        }

        public override void PlayerDisconnect()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                AlignedPlayer widow = Main.LocalPlayer.GetModPlayer<AlignedPlayer>();
                if (Player.whoAmI == widow.PartnerID)
                {
                    widow.Decoherence();
                }
            }
        }

        public override void OnRespawn()
        {
            UpdateGravity();
        }

        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            if (IsEntangled && triggersSet.Up && !JustPressedUp && (Player.gravControl || Player.gravControl2) && !Player.pulley)
            {
                Player.gravDir = IsFlipped ? -1f : 1f;
                FlipGravity();
            }

            JustPressedUp = triggersSet.Up;
        }

        public override void PostHurt(Player.HurtInfo info)
        {
            if (config.PainFlip) { FlipGravity(); }
        }

        public override void PreUpdateMovement()
        {
            if (config.GravityJump && Player.justJumped && Player.whoAmI == Main.myPlayer)
            {
                FlipGravity();
            }
        }

        private void FlipGravity()
        {
            if (IsEntangled)
            {
                IsFlipped = !IsFlipped;
                UpdateGravity();
            }
        }

        private void UpdateGravity()
        {
            if (IsEntangled)
            {
                Player partner = Main.player[PartnerID];

                Player.forcedGravity = IsFlipped ? int.MaxValue : 0; 
                partner.forcedGravity = !IsFlipped ? int.MaxValue : 0;

                Player.gravDir = IsFlipped ? -1f : 1f;
                partner.gravDir = IsFlipped ? -1f : 1f;
                
                ModPacket packet = Mod.GetPacket();
                packet.Write((byte) MessageType.Flux);
                packet.Write(IsFlipped);
                packet.Send();
            }
        }

        private void EmitRizz()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) {
                ModPacket packet = Mod.GetPacket();
                packet.Write((byte) MessageType.Rizz);
                packet.Send();
            }
        }
        
        public async void Entangle(int partnerID, bool isFlipped) {
            PartnerID = partnerID;
            Logger.Info($"{Player.name} has entangled with {Main.player[partnerID].name}");
            Main.NewText($"[c/{config.NoticeColor}:Your mass is now quantum entangled with ][c/{config.PlayerColor}:{Main.player[partnerID].name}]");
            Main.NewText($"[c/{config.WarningColor}:Prepare for gravitational desynchronisation...]");
            
            IsFlipped = isFlipped;
            if (IsFlipped) { await Task.Delay(3000); UpdateGravity(); }
        }

        public void Decoherence()
        {
            Logger.Info($"{Player.name} is experiencing decoherence.");
            
            ModPacket packet = Mod.GetPacket();
            packet.Write((byte) MessageType.Decoherence);
            packet.Send();

            PartnerID = -1;
            Player.forcedGravity = 0;
            Terraria.Chat.ChatHelper.SendChatMessageToClient(
                NetworkText.FromLiteral($"[c/{config.NoticeColor}:Quantum decoherence has occurred. You are no longer entangled with your partner.]"),
                Color.White,
                Player.whoAmI
            );
        }
    }

    public class GravityLink : Mod
        {
            private void HandleFlux(BinaryReader reader, int whoAmI)
            {
                AlignedPlayer player;
                AlignedPlayer partner;
                bool isFlipped;

                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        isFlipped = reader.ReadBoolean();
                        player = Main.player[whoAmI].GetModPlayer<AlignedPlayer>();
                        partner = Main.player[player.PartnerID].GetModPlayer<AlignedPlayer>();

                        partner.IsFlipped = player.IsFlipped;
                        player.IsFlipped = !player.IsFlipped;
                        
                        partner.Player.forcedGravity = partner.IsFlipped ? int.MaxValue : 0;
                        player.Player.forcedGravity = player.IsFlipped ? int.MaxValue : 0;

                        partner.Player.gravDir = partner.IsFlipped ? -1f : 1f;
                        player.Player.gravDir = player.IsFlipped ? -1f : 1f;

                        ModPacket packet = GetPacket();
                        packet.Write((byte) MessageType.Flux);
                        packet.Write((byte) whoAmI);
                        packet.Write((byte) player.PartnerID);
                        packet.Write(isFlipped);
                        packet.Send(-1);
                        break;
                    
                    case NetmodeID.MultiplayerClient:
                        int playerID = reader.ReadByte();
                        int partnerID = reader.ReadByte();
                        isFlipped = reader.ReadBoolean();

                        Main.player[playerID].forcedGravity = isFlipped ? int.MaxValue : 0;
                        Main.player[partnerID].forcedGravity = !isFlipped ? int.MaxValue : 0;

                        Main.player[playerID].gravDir = isFlipped ? -1f : 1f;
                        Main.player[partnerID].gravDir = !isFlipped ? -1f : 1f;

                        if (partnerID == Main.LocalPlayer.whoAmI)
                        {
                            partner = Main.LocalPlayer.GetModPlayer<AlignedPlayer>();
                            partner.IsFlipped = !isFlipped;
                        }
                        break;
                }
            }

            private void ForwardRizz(int whoAmI)
            {
                AlignedPlayer rizzler = Main.player[whoAmI].GetModPlayer<AlignedPlayer>();
                AlignedPlayer rizzed;
                
                foreach (Player player in Main.ActivePlayers)
                {
                    rizzed = player.GetModPlayer<AlignedPlayer>();

                    if (rizzler == rizzed) { continue; }

                    if (!rizzed.IsEntangled) {
                        rizzed.PartnerID = whoAmI;
                        rizzler.PartnerID = rizzed.Player.whoAmI;

                        bool randomPolarity = new Random().Next(2) == 0;
                        
                        ModPacket rizzedPacket = GetPacket();
                        rizzedPacket.Write((byte) MessageType.Rizz);
                        rizzedPacket.Write((byte) whoAmI);
                        rizzedPacket.Write(randomPolarity);
                        rizzedPacket.Send(rizzed.Player.whoAmI);

                        ModPacket rizzlerPacket = GetPacket();
                        rizzlerPacket.Write((byte) MessageType.Rizz);
                        rizzlerPacket.Write((byte) rizzed.Player.whoAmI);
                        rizzlerPacket.Write(!randomPolarity);
                        rizzlerPacket.Send(whoAmI);
                    }
                }
            }

            private void HandleRizz(BinaryReader reader, int whoAmI)
            {
                switch (Main.netMode)
                {
                    case NetmodeID.Server:
                        ForwardRizz(whoAmI);
                        break;
                    case NetmodeID.MultiplayerClient:
                        int partnerID = reader.ReadByte();
                        bool isFlipped = reader.ReadBoolean();
                        AlignedPlayer rizzed = Main.LocalPlayer.GetModPlayer<AlignedPlayer>();
                        rizzed.Entangle(partnerID, isFlipped);
                        break;
                }  
            }

            public override void HandlePacket(BinaryReader reader, int whoAmI)
            {
                MessageType msgType = (MessageType) reader.ReadByte();
                
                switch (msgType)
                {
                    case MessageType.Decoherence:
                        AlignedPlayer widow = Main.player[whoAmI].GetModPlayer<AlignedPlayer>();
                        widow.PartnerID = -1;
                        break;

                    case MessageType.Flux:
                        HandleFlux(reader, whoAmI);
                        break;

                    case MessageType.Rizz:
                        HandleRizz(reader, whoAmI);
                        break;
                }
            }
    }

    public class Axiom : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [Header("Triggers")]
 
        [DefaultValue(false)]
        public bool PainFlip { get; set; }

        [DefaultValue(false)]
        public bool GravityJump { get; set; }

        [Header("Theme")]

        [DefaultValue("00FFD1")]
        public string NoticeColor { get; set; }

        [DefaultValue("FF0F0F")]
        public string WarningColor { get; set; }

        [DefaultValue("5200FF")]
        public string PlayerColor { get; set; }
    }

    internal enum MessageType : byte
    {
        Decoherence,
        Flux,
        Rizz,
    }
}