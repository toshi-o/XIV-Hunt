﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace FFXIV_GameSense
{
    internal class ChatMessage
    {
        internal DateTime Timestamp { get; set; }
        private uint Epoch => Convert.ToUInt32(DateTimeToUnixTimestamp(Timestamp));
        internal ChatChannel Channel { get; set; }
        internal ChatFilter Filter { get; set; }
        internal string Sender { get; set; }
        internal byte[] Message { get; set; }
        private const string possep = "<pos>";
        private static readonly Dictionary<string, byte[]> Tags = new Dictionary<string, byte[]>
        {
            { "<Emphasis>",  new byte[] { 0x02, 0x1A, 0x02, 0x02, 0x03 } },
            { "</Emphasis>",  new byte[] { 0x02, 0x1A, 0x02, 0x01, 0x03 } },
            { "<SoftHyphen/>", new byte[] { 0x02, 0x16, 0x01, 0x03 } },
            { "<Indent/>", new byte[] { 0x02, 0x1D, 0x01, 0x03 } },
            { "<22/>",  new byte[] { 0x02, 0x16, 0x01, 0x03 } }
        };
        private static readonly byte[] arrow = new byte[] { 0xEE, 0x82, 0xBB, 0x02, 0x13, 0x02, 0xEC, 0x03 };
        private static readonly Dictionary<int, byte[]> RarityColors = new Dictionary<int, byte[]>
        {
            { 1, new byte[] { 0xF3, 0xF3, 0xF3 } },
            { 2, new byte[] { 0xC0, 0xFF, 0xC0 } },
            { 3, new byte[] { 0x59, 0x90, 0xFF } },
            { 4, new byte[] { 0xB3, 0x8C, 0xFF } },
            { 7, new byte[] { 0xFA, 0x89, 0xB6 } }
        };

        /// <summary>
        /// Default constructor. Sets timestamp to now and channel to Echo, message=null;
        /// </summary>
        internal ChatMessage()
        {
            if (Program.mem != null)
                Timestamp = Program.mem.GetServerUtcTime();
            else
                Timestamp = DateTime.UtcNow;
            Channel = ChatChannel.Echo;
            Filter = ChatFilter.Unknown;
            Message = null;
        }

        /// <summary>
        /// First 4 bytes should be unix timestamp.
        /// 5th byte should be ChatChannel.
        /// 6th byte should be ChatFilter(unknown).
        /// 9th byte should be 0x3A, followed by sender name (UTF-8), followed by 0x3A.
        /// The rest is the message, including payloads.
        /// </summary>
        /// <param name="arr">Byte array should be longer than 10</param>
        internal ChatMessage(byte[] arr)
        {
            if (arr.Length < 10)
            {
                return;
            }
            Timestamp = UnixTimeStampToDateTime(BitConverter.ToUInt32(arr.Take(4).ToArray(), 0));
            Channel = (ChatChannel)arr[4];
            Filter = (ChatFilter)arr[5];//hmm
            Sender = Encoding.UTF8.GetString(arr, 9, Array.FindIndex(arr.Skip(9).ToArray(), x => x == 0x3A));
            int pos = arr.Skip(9).ToList().IndexOf(0x3A) + 10;
            Message = arr.Skip(pos).ToArray();
        }

        private static DateTime UnixTimeStampToDateTime(uint unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (TimeZoneInfo.ConvertTimeToUtc(dateTime) - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal byte[] ToArray()
        {
            List<byte> a = new List<byte>();
            a.AddRange(BitConverter.GetBytes(Epoch));
            a.Add((byte)Channel);
            a.Add((byte)Filter);
            a.AddRange(new byte[] { 0x00, 0x00 });
            a.Add(Convert.ToByte(':'));
            if (!string.IsNullOrEmpty(Sender))
                a.AddRange(Encoding.UTF8.GetBytes(Sender));
            a.Add(Convert.ToByte(':'));
            if (Message != null)
                a.AddRange(ReplaceTags(Message));
            return a.ToArray();
        }

        internal static byte[] ReplaceTags(byte[] msg)
        {
            foreach (KeyValuePair<string, byte[]> kvp in Tags)
                msg = msg.ReplaceSequence(Encoding.UTF8.GetBytes(kvp.Key), kvp.Value);
            return msg;
        }

        internal string GetCleanString
        {
            get { return CleanString(Encoding.UTF8.GetString(Message)); }
        }

        //internal static string GetCleanString(byte[] msg)
        //{
        //    return CleanString(Encoding.UTF8.GetString(msg));
        //}

        private static string CleanString(string input)
        {
            return new string(input.Where(value => (value >= 0x0020 && value <= 0xD7FF) || (value >= 0xE000 && value <= 0xFFFD) || value == 0x0009 || value == 0x000A || value == 0x000D).ToArray());
        }

        internal static ChatMessage MakeItemChatMessage(XIVDB.Item Item, string prepend = "", string postpend = "")
        {
            var cm = new ChatMessage();
            var idba = BitConverter.GetBytes(Item.Id);
            Array.Reverse(idba);
            var raritycolor = RarityColors.ContainsKey(Item.Rarity) ? RarityColors[Item.Rarity] : RarityColors.First().Value;
            var ItemHeader1And2 = new byte[] { 0x02, 0x13, 0x06, 0xFE, 0xFF }.Concat(raritycolor).Concat(new byte[] { 0x03, 0x02, 0x27, 0x07, 0x03, 0xF2 }).ToArray();
            var ItemHeader3 = new byte[] { 0x02, 0x01, 0x03 };
            if (Item.Id <= byte.MaxValue)//Currencies
            {
                ItemHeader1And2 = ItemHeader1And2.Take(ItemHeader1And2.Count() - 1).ToArray();
                Array.Reverse(idba);
                idba[0]++;
                idba[1] = 0x02;
                ItemHeader3 = ItemHeader3.Skip(1).ToArray();
                ItemHeader1And2[11] = (byte)(ItemHeader1And2[11] - 2);
            }
            else if (idba.Last() == 0x00)
            {
                ItemHeader1And2[ItemHeader1And2.Length - 1]--;
                idba = new byte[] { idba[0] };
                ItemHeader1And2[11]--;
            }
            ItemHeader1And2 = ItemHeader1And2.Concat(idba).ToArray();
            //                                            ?     ?     R     G     B
            var color = new byte[] { 0x02, 0x13, 0x06, 0xFE, 0xFF, 0xFF, 0x7B, 0x1A, 0x03 };
            var end = new byte[] { 0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03, 0x02, 0x13, 0x02, 0xEC, 0x03 };
            if (Array.IndexOf(ItemHeader1And2, 0x00) > -1)
            {
                Debug.WriteLine("ItemHeader contains 0x00. Params: " + Item.Id);
                return null;
            }
            cm.Message = Encoding.UTF8.GetBytes(prepend).Concat(ItemHeader1And2).Concat(ItemHeader3).Concat(color).Concat(arrow).Concat(Encoding.UTF8.GetBytes(Item.Name)).Concat(end).ToArray();
            if (!string.IsNullOrEmpty(postpend))
                cm.Message = cm.Message.Concat(Encoding.UTF8.GetBytes(postpend)).Concat(new byte[] { 0x00 }).ToArray();
            else
                cm.Message = cm.Message.Concat(new byte[] { 0x00 }).ToArray();
            return cm;
        }

        internal static ChatMessage MakePosChatMessage(string prepend, ushort zoneId, float x, float y, string postpend = "", ushort mapId = 0)
        {
            var cm = new ChatMessage();
            var pos = new byte[] { 0x02, 0x27, 0x12, 0x04 };
            var posZone = XIVDB.GameResources.GetMapMarkerZoneId(zoneId, mapId);
            var posX = CoordToFlagPosCoord(x);
            var posY = CoordToFlagPosCoord(y);
            //z does not appear to be used for the link; only for the text
            var posPost = new byte[] { 0xFF, 0x01, 0x03 };//end +/ terminator
            pos = pos.Concat(posZone).Concat(posX).Concat(posY).Concat(posPost).ToArray();
            pos[2] = Convert.ToByte(pos.Length - 3);
            //                                            ?     ?     R     G     B
            var color = new byte[] { 0x02, 0x13, 0x06, 0xFE, 0xFF, 0xA3, 0xEA, 0xF3, 0x03 };
            var end = new byte[] { 0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03 };
            if (Array.IndexOf(posPost, 0x00) > -1)
            {
                Debug.WriteLine("posPost contains 0x00. Params: " + zoneId + " " + x + " " + y);
                return null;
            }

            if (prepend.Contains(possep))
            {
                string[] split = prepend.Split(new string[] { possep }, 2, StringSplitOptions.None);
                prepend = split.First();
                postpend = split.Last() + postpend;
            }
            else if (postpend.Contains(possep))
            {
                string[] split = postpend.Split(new string[] { possep }, 2, StringSplitOptions.None);
                prepend = prepend + split.First();
                postpend = split.Last();
            }

            cm.Message = Encoding.UTF8.GetBytes(prepend).Concat(pos).Concat(color).Concat(arrow).Concat(Encoding.UTF8.GetBytes(XIVDB.GameResources.GetZoneName(zoneId) + " ( " + Combatant.GetXReadable(x, zoneId).ToString("0.0").Replace(',', '.') + "  , " + Combatant.GetYReadable(y, zoneId).ToString("0.0").Replace(',', '.') + " )")).Concat(end).ToArray();
            if (!string.IsNullOrEmpty(postpend))
                cm.Message = cm.Message.Concat(Encoding.UTF8.GetBytes(postpend)).Concat(new byte[] { 0x00 }).ToArray();
            else
                cm.Message = cm.Message.Concat(new byte[] { 0x00 }).ToArray();

            return cm;
        }

        private static byte[] CoordToFlagPosCoord(float coordinate)
        {
            coordinate *= 1000;
            if (coordinate == 0f)//only if c > -0.256f && c < 0.256f, either way, default case on switch later on will take care of it
                coordinate++;
            byte[] t = BitConverter.GetBytes((int)coordinate);
            while (t.Last() == 0)//trim big-endian leading zeros
                Array.Resize(ref t, t.Length - 1);
            Array.Reverse(t);
            byte[] temp;
            switch (t.Length)
            {
                case 4: temp = new byte[] { 0xFE }; break;
                case 2: temp = new byte[] { 0xF2 }; break;
                case 3: temp = new byte[] { 0xF6 }; break;
                default: temp = new byte[] { 0xF2, 0x01 }; break;//must be minimum 3 bytes long
            }
            t = temp.Concat(t).ToArray();
            //0x00 prevents the game continuing reading the coordinate payload.
            //workaround
            for (int i = 0; i < t.Length; i++)
                if (t[i] == 0x00)
                    t[i]++;
            return t;
        }
    }

    enum ChatFilter : byte
    {
        Unknown = 0x00,
    }

    internal enum ChatChannel : byte
    {
        Unknown = 0x00,
        MoTDorSA1 = 0x03,
        //MoTDorSA2 = 0x04,
        //MoTDorSA3 = 0x05,
        //MoTDorSA4 = 0x06,
        //MoTDorSA5 = 0x07,
        //MoTDorSA6 = 0x08,
        //MoTDorSA7 = 0x09,
        Say = 0x0A,
        Shout = 0x0B,
        OutTell = 0x0C,
        IncTell = 0x0D,
        Party = 0x0E,
        Alliance = 0x0F,
        Linkshell1 = 0x10,
        Linkshell2 = 0x11,
        Linkshell3 = 0x12,
        Linkshell4 = 0x13,
        Linkshell5 = 0x14,
        Linkshell6 = 0x15,
        Linkshell7 = 0x16,
        Linkshell8 = 0x17,
        FC = 0x18,
        StandardEmote = 0x1D,
        NoviceNetwork = 0x1B,
        CustomEmote = 0x1C,
        Yell = 0x1E,
        FinishedCastingUsingAbility = 0x2B,
        Echo = 0x38,
        SystemMessage = 0x39,//example: you dissolve a party, you invite 'player name' to party, player 'player name' joins the party, updating online status to away
        Defeats = 0x3A,
        Error = 0x3C,//example: unable to change gear
        NPCChat = 0x3E,
        ObtainsAndConverts = 0x40,
        ExperienceAndLevel = 0x41,
        ItemRolls = 0x45,
        PFRecruitmentNoficiation = 0x48, // Of the 38 parties currently recruiting, all match your search conditions.
        LoginsAndLogouts = 0xA9,
        BuffLossAndGains = 0xAE,
        EffectGains = 0xAF
    }
}
