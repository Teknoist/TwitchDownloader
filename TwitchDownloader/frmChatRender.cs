﻿using Accord.Video.FFMPEG;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwitchDownloader
{
    public partial class frmChatRender : Form
    {
        public frmChatRender()
        {
            InitializeComponent();
        }

        private void BntColor_Click(object sender, EventArgs e)
        {
            colorDialog.Color = Color.Black;

            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                textColor.Text = ToHexValue(colorDialog.Color);
            }
        }

        public string ToHexValue(Color color)
        {
            return "#" + color.R.ToString("X2") +
                         color.G.ToString("X2") +
                         color.B.ToString("X2");
        }

        private void BtnRender_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();

            saveFileDialog.Filter = "MP4 Files | *.mp4";
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                RenderOptions info = new RenderOptions(textJSON.Text, saveFileDialog.FileName, colorDialog.Color, Int32.Parse(textHeight.Text), Int32.Parse(textWidth.Text), checkBTTV.Checked, checkFFZ.Checked);

                backgroundRenderManager.RunWorkerAsync(info);
                btnRender.Enabled = false;
            }
        }

        private void BackgroundRenderManager_DoWork(object sender, DoWorkEventArgs e)
        {
            RenderOptions renderOptions = (RenderOptions)e.Argument;
            JObject chatJson = JObject.Parse(File.ReadAllText(renderOptions.json_path));
            List<TwitchComment> finalComments = new List<TwitchComment>();
            List<ThirdPartyEmote> thirdPartyEmotes = new List<ThirdPartyEmote>();
            List<ChatBadge> chatBadges = new List<ChatBadge>();
            List<KeyValuePair<int, Image>> chatEmotes = new List<KeyValuePair<int, Image>>();
            Random rand = new Random();
            string[] defaultColors = { "#FF0000", "#0000FF", "#00FF00", "#B22222", "#FF7F50", "#9ACD32", "#FF4500", "#2E8B57", "#DAA520", "#D2691E", "#5F9EA0", "#1E90FF", "#FF69B4", "#8A2BE2", "#00FF7F" };

            backgroundRenderManager.ReportProgress(0, "Fetching Chat Badges");
            GetChatBadges(chatBadges, chatJson["streamer"]);
            backgroundRenderManager.ReportProgress(0, "Fetching Emotes");
            GetEmotes(chatEmotes, chatJson["comments"]);
            backgroundRenderManager.ReportProgress(0, "Fetching Third Party Emotes");
            GetThirdPartyEmotes(thirdPartyEmotes, renderOptions, chatJson["streamer"]);

            Size canvasSize = new Size(renderOptions.chat_width, 32);
            Font messageFont = new Font("Arial", 9.0F, FontStyle.Bold);
            Font nameFont = new Font("Arial", 9.0F, FontStyle.Bold);
            Font emojiFont = new Font("Segoe UI Emoji", 9.0F);

            backgroundRenderManager.ReportProgress(0, "Rendering Comments");
            foreach (var comment in chatJson["comments"])
            {
                string userName = comment["commenter"]["display_name"].ToString();
                Point drawPos = new Point(2, canvasSize.Height / 4);
                Color userColor = ColorTranslator.FromHtml(comment["message"]["user_color"] != null ? comment["message"]["user_color"].ToString() : defaultColors[rand.Next(0, defaultColors.Length)]);
                Bitmap sectionImage = new Bitmap(canvasSize.Width, canvasSize.Height);
                Graphics g = Graphics.FromImage(sectionImage);
                List<Section> messageSections = new List<Section>();
                List<GifEmote> currentGifEmotes = new List<GifEmote>();
                Section currentSection = new Section(sectionImage, false, currentGifEmotes);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.FillRectangle(new SolidBrush(renderOptions.background_color), 0, 0, canvasSize.Width, canvasSize.Height);

                DrawBadges(g, chatBadges, comment, ref canvasSize, ref drawPos);
                DrawUsername(g, nameFont, userName, userColor, ref canvasSize, ref drawPos);
                DrawMessage(g, renderOptions, sectionImage, messageSections, currentGifEmotes, currentSection, finalComments, messageFont, emojiFont, chatEmotes, thirdPartyEmotes, comment, userName, userColor, ref canvasSize, ref drawPos);
            }

            backgroundRenderManager.ReportProgress(0, "Rendering Video 0%");
            RenderVideo(renderOptions, finalComments, chatJson["comments"]);
        }

        private void RenderVideo(RenderOptions renderOptions, List<TwitchComment> finalComments, JToken comments)
        {
            Bitmap canvas = new Bitmap(renderOptions.chat_width, renderOptions.chat_height);
            Graphics gcan = Graphics.FromImage(canvas);
            int videoStart = (int)Math.Floor(comments.First["content_offset_seconds"].ToObject<double>());
            int duration = (int)Math.Ceiling(comments.Last["content_offset_seconds"].ToObject<double>()) - videoStart;
            int globalTick = 0;
            List<GifEmote> displayedGifs = new List<GifEmote>();
            using (var vFWriter = new VideoFileWriter())
            {
                // create new video file
                vFWriter.Open(renderOptions.save_path, renderOptions.chat_width, renderOptions.chat_height, 60, VideoCodec.H264, 3000000);
                gcan.FillRectangle(new SolidBrush(renderOptions.background_color), 0, 0, renderOptions.chat_width, renderOptions.chat_height);
                gcan = Graphics.FromImage(canvas);
                for (int i = videoStart; i < videoStart + duration; i++)
                {
                    int height = 0;
                    for (int w = 0; w < 60; w++)
                    {
                        if (w == 0)
                        {
                            int y = 0;
                            List<GifEmote> newly_added = new List<GifEmote>();
                            List<GifEmote> old = new List<GifEmote>(displayedGifs);
                            for (int j = 0; j < finalComments.Count; j++)
                            {
                                if (Math.Floor(finalComments[j].secondsOffset) == i)
                                {
                                    foreach (var emote in finalComments[j].gifEmotes)
                                    {
                                        GifEmote newGif = new GifEmote(new Point(emote.offset.X, emote.offset.Y + height), emote.name, emote.image);
                                        displayedGifs.Add(newGif);
                                        newly_added.Add(newGif);
                                    }
                                    height += finalComments[j].section.Height;
                                }
                            }
                            foreach (var emote in old)
                                emote.offset = new Point(emote.offset.X, emote.offset.Y - height);
                            foreach (var emote in newly_added)
                                emote.offset = new Point(emote.offset.X, (renderOptions.chat_height - height) + emote.offset.Y);

                            if (height > 0)
                            {
                                Bitmap buffer = new Bitmap(renderOptions.chat_width, height);
                                Graphics bg = Graphics.FromImage(buffer);

                                for (int j = 0; j < finalComments.Count; j++)
                                {
                                    if (Math.Floor(finalComments[j].secondsOffset) == i)
                                    {
                                        bg.DrawImage(finalComments[j].section, 0, y);
                                        y += finalComments[j].section.Height;
                                    }
                                }
                                gcan = Graphics.FromImage(canvas);
                                gcan.DrawImage(canvas, 0, -height);
                                gcan.DrawImage(buffer, 0, renderOptions.chat_height - height);
                            }
                        }
                        List<GifEmote> to_remove = new List<GifEmote>();
                        foreach (var emote in displayedGifs)
                        {
                            if (emote.offset.Y < -emote.image.Width - renderOptions.chat_height)
                            {
                                to_remove.Add(emote);
                            }
                            else
                            {
                                int frame = (15 * globalTick / emote.fps) % emote.frames;
                                FrameDimension dim = new FrameDimension(emote.image.FrameDimensionsList[0]);
                                emote.image.SelectActiveFrame(dim, frame);
                                gcan.DrawImage(emote.image, emote.offset);
                            }
                        }

                        foreach (var emote in to_remove)
                        {
                            displayedGifs.Remove(emote);
                        }

                        vFWriter.WriteVideoFrame(canvas);
                        foreach (var emote in displayedGifs)
                        {
                            gcan.FillRectangle(new SolidBrush(renderOptions.background_color), emote.offset.X, emote.offset.Y, emote.image.Width, emote.image.Height);
                        }
                        globalTick += 1;
                        int percent = (int)Math.Floor(globalTick / (duration * 60.0) * 100.0);
                        backgroundRenderManager.ReportProgress(percent, String.Format("Rendering Video {0}%", percent));
                    }
                }
                vFWriter.Close();
            }
        }

        private void DrawMessage(Graphics g, RenderOptions renderOptions, Bitmap sectionImage, List<Section> messageSections, List<GifEmote> currentGifEmotes, Section currentSection, List<TwitchComment> finalComments, Font messageFont, Font emojiFont, List<KeyValuePair<int, Image>> chatEmotes, List<ThirdPartyEmote> thirdPartyEmotes, JToken comment, string userName, Color userColor, ref Size canvasSize, ref Point drawPos)
        {
            if (comment["source"].ToString() != "chat")
                return;

            string emojiRegex = "(?:0\x20E3|1\x20E3|2\x20E3|3\x20E3|4\x20E3|5\x20E3|6\x20E3|7\x20E3|8\x20E3|9\x20E3|#\x20E3|\\*\x20E3|\xD83C(?:\xDDE6\xD83C(?:\xDDE8|\xDDE9|\xDDEA|\xDDEB|\xDDEC|\xDDEE|\xDDF1|\xDDF2|\xDDF4|\xDDF6|\xDDF7|\xDDF8|\xDDF9|\xDDFA|\xDDFC|\xDDFD|\xDDFF)|\xDDE7\xD83C(?:\xDDE6|\xDDE7|\xDDE9|\xDDEA|\xDDEB|\xDDEC|\xDDED|\xDDEE|\xDDEF|\xDDF1|\xDDF2|\xDDF3|\xDDF4|\xDDF6|\xDDF7|\xDDF8|\xDDF9|\xDDFB|\xDDFC|\xDDFE|\xDDFF)|\xDDE8\xD83C(?:\xDDE6|\xDDE8|\xDDE9|\xDDEB|\xDDEC|\xDDED|\xDDEE|\xDDF0|\xDDF1|\xDDF2|\xDDF3|\xDDF4|\xDDF5|\xDDF7|\xDDFA|\xDDFB|\xDDFC|\xDDFD|\xDDFE|\xDDFF)|\xDDE9\xD83C(?:\xDDEA|\xDDEC|\xDDEF|\xDDF0|\xDDF2|\xDDF4|\xDDFF)|\xDDEA\xD83C(?:\xDDE6|\xDDE8|\xDDEA|\xDDEC|\xDDED|\xDDF7|\xDDF8|\xDDF9|\xDDFA)|\xDDEB\xD83C(?:\xDDEE|\xDDEF|\xDDF0|\xDDF2|\xDDF4|\xDDF7)|\xDDEC\xD83C(?:\xDDE6|\xDDE7|\xDDE9|\xDDEA|\xDDEB|\xDDEC|\xDDED|\xDDEE|\xDDF1|\xDDF2|\xDDF3|\xDDF5|\xDDF6|\xDDF7|\xDDF8|\xDDF9|\xDDFA|\xDDFC|\xDDFE)|\xDDED\xD83C(?:\xDDF0|\xDDF2|\xDDF3|\xDDF7|\xDDF9|\xDDFA)|\xDDEE\xD83C(?:\xDDE8|\xDDE9|\xDDEA|\xDDF1|\xDDF2|\xDDF3|\xDDF4|\xDDF6|\xDDF7|\xDDF8|\xDDF9)|\xDDEF\xD83C(?:\xDDEA|\xDDF2|\xDDF4|\xDDF5)|\xDDF0\xD83C(?:\xDDEA|\xDDEC|\xDDED|\xDDEE|\xDDF2|\xDDF3|\xDDF5|\xDDF7|\xDDFC|\xDDFE|\xDDFF)|\xDDF1\xD83C(?:\xDDE6|\xDDE7|\xDDE8|\xDDEE|\xDDF0|\xDDF7|\xDDF8|\xDDF9|\xDDFA|\xDDFB|\xDDFE)|\xDDF2\xD83C(?:\xDDE6|\xDDE8|\xDDE9|\xDDEA|\xDDEB|\xDDEC|\xDDED|\xDDF0|\xDDF1|\xDDF2|\xDDF3|\xDDF4|\xDDF5|\xDDF6|\xDDF7|\xDDF8|\xDDF9|\xDDFA|\xDDFB|\xDDFC|\xDDFD|\xDDFE|\xDDFF)|\xDDF3\xD83C(?:\xDDE6|\xDDE8|\xDDEA|\xDDEB|\xDDEC|\xDDEE|\xDDF1|\xDDF4|\xDDF5|\xDDF7|\xDDFA|\xDDFF)|\xDDF4\xD83C\xDDF2|\xDDF5\xD83C(?:\xDDE6|\xDDEA|\xDDEB|\xDDEC|\xDDED|\xDDF0|\xDDF1|\xDDF2|\xDDF3|\xDDF7|\xDDF8|\xDDF9|\xDDFC|\xDDFE)|\xDDF6\xD83C\xDDE6|\xDDF7\xD83C(?:\xDDEA|\xDDF4|\xDDF8|\xDDFA|\xDDFC)|\xDDF8\xD83C(?:\xDDE6|\xDDE7|\xDDE8|\xDDE9|\xDDEA|\xDDEC|\xDDED|\xDDEE|\xDDEF|\xDDF0|\xDDF1|\xDDF2|\xDDF3|\xDDF4|\xDDF7|\xDDF8|\xDDF9|\xDDFB|\xDDFD|\xDDFE|\xDDFF)|\xDDF9\xD83C(?:\xDDE6|\xDDE8|\xDDE9|\xDDEB|\xDDEC|\xDDED|\xDDEF|\xDDF0|\xDDF1|\xDDF2|\xDDF3|\xDDF4|\xDDF7|\xDDF9|\xDDFB|\xDDFC|\xDDFF)|\xDDFA\xD83C(?:\xDDE6|\xDDEC|\xDDF2|\xDDF8|\xDDFE|\xDDFF)|\xDDFB\xD83C(?:\xDDE6|\xDDE8|\xDDEA|\xDDEC|\xDDEE|\xDDF3|\xDDFA)|\xDDFC\xD83C(?:\xDDEB|\xDDF8)|\xDDFD\xD83C\xDDF0|\xDDFE\xD83C(?:\xDDEA|\xDDF9)|\xDDFF\xD83C(?:\xDDE6|\xDDF2|\xDDFC)))|[\xA9\xAE\x203C\x2049\x2122\x2139\x2194-\x2199\x21A9\x21AA\x231A\x231B\x2328\x23CF\x23E9-\x23F3\x23F8-\x23FA\x24C2\x25AA\x25AB\x25B6\x25C0\x25FB-\x25FE\x2600-\x2604\x260E\x2611\x2614\x2615\x2618\x261D\x2620\x2622\x2623\x2626\x262A\x262E\x262F\x2638-\x263A\x2648-\x2653\x2660\x2663\x2665\x2666\x2668\x267B\x267F\x2692-\x2694\x2696\x2697\x2699\x269B\x269C\x26A0\x26A1\x26AA\x26AB\x26B0\x26B1\x26BD\x26BE\x26C4\x26C5\x26C8\x26CE\x26CF\x26D1\x26D3\x26D4\x26E9\x26EA\x26F0-\x26F5\x26F7-\x26FA\x26FD\x2702\x2705\x2708-\x270D\x270F\x2712\x2714\x2716\x271D\x2721\x2728\x2733\x2734\x2744\x2747\x274C\x274E\x2753-\x2755\x2757\x2763\x2764\x2795-\x2797\x27A1\x27B0\x27BF\x2934\x2935\x2B05-\x2B07\x2B1B\x2B1C\x2B50\x2B55\x3030\x303D\x3297\x3299]|\xD83C[\xDC04\xDCCF\xDD70\xDD71\xDD7E\xDD7F\xDD8E\xDD91-\xDD9A\xDE01\xDE02\xDE1A\xDE2F\xDE32-\xDE3A\xDE50\xDE51\xDF00-\xDF21\xDF24-\xDF93\xDF96\xDF97\xDF99-\xDF9B\xDF9E-\xDFF0\xDFF3-\xDFF5\xDFF7-\xDFFF]|\xD83D[\xDC00-\xDCFD\xDCFF-\xDD3D\xDD49-\xDD4E\xDD50-\xDD67\xDD6F\xDD70\xDD73-\xDD79\xDD87\xDD8A-\xDD8D\xDD90\xDD95\xDD96\xDDA5\xDDA8\xDDB1\xDDB2\xDDBC\xDDC2-\xDDC4\xDDD1-\xDDD3\xDDDC-\xDDDE\xDDE1\xDDE3\xDDEF\xDDF3\xDDFA-\xDE4F\xDE80-\xDEC5\xDECB-\xDED0\xDEE0-\xDEE5\xDEE9\xDEEB\xDEEC\xDEF0\xDEF3]|\xD83E[\xDD10-\xDD18\xDD80-\xDD84\xDDC0]";
            foreach (var fragment in comment["message"]["fragments"])
            {
                if (fragment["emoticon"] == null)
                {
                    string[] fragmentParts = fragment["text"].ToString().Split(' ');
                    for (int i = 0; i < fragmentParts.Length; i++)
                    {
                        bool isThirdPartyEmote = false;

                        ThirdPartyEmote currentEmote = null;
                        foreach (var thirdPartyEmote in thirdPartyEmotes)
                        {
                            if (fragmentParts[i] == thirdPartyEmote.name)
                            {
                                isThirdPartyEmote = true;
                                currentEmote = thirdPartyEmote;
                            }
                        }

                        if (isThirdPartyEmote)
                        {
                            if (drawPos.X + currentEmote.emote.Width > canvasSize.Width)
                                AddNewSection(ref messageSections, ref renderOptions, ref currentGifEmotes, ref currentSection, ref g, ref sectionImage, ref canvasSize, ref drawPos);

                            if (currentEmote.imageType == "gif")
                            {
                                GifEmote emote = new GifEmote(new Point(drawPos.X + 2, 0), currentEmote.name, currentEmote.emote);
                                currentGifEmotes.Add(emote);
                                currentSection.hasEmote = true;
                                drawPos.X += currentEmote.emote.Width + 2;
                            }
                            else
                            {
                                currentSection.hasEmote = true;
                                if (currentEmote.emote.Height > 32)
                                    currentEmote.emote = ScaleImage(currentEmote.emote, 9999, 32);
                                g.DrawImage(currentEmote.emote, drawPos.X + 2, drawPos.Y - 6, currentEmote.emote.Width, currentEmote.emote.Height);
                                drawPos.X += currentEmote.emote.Width + 2;
                            }
                        }
                        else
                        {
                            string output = fragmentParts[i].Trim();

                            if (Regex.Match(output, emojiRegex).Success)
                            {
                                int inputWidth = (int)Math.Ceiling(g.MeasureString(output, emojiFont, 0, StringFormat.GenericTypographic).Width);

                                if (drawPos.X + inputWidth + 3 > canvasSize.Width)
                                    AddNewSection(ref messageSections, ref renderOptions, ref currentGifEmotes, ref currentSection, ref g, ref sectionImage, ref canvasSize, ref drawPos);
                                g.DrawString(output, emojiFont, new SolidBrush(Color.White), drawPos.X, drawPos.Y + 2);
                                drawPos.X += inputWidth + 5;
                                
                            }
                            else
                            {
                                int inputWidth = (int)Math.Ceiling(g.MeasureString(output, messageFont, 0, StringFormat.GenericTypographic).Width);

                                if (drawPos.X + inputWidth + 3 > canvasSize.Width)
                                    AddNewSection(ref messageSections, ref renderOptions, ref currentGifEmotes, ref currentSection, ref g, ref sectionImage, ref canvasSize, ref drawPos);
                                g.DrawString(output, messageFont, new SolidBrush(Color.White), drawPos.X, drawPos.Y + 2);
                                drawPos.X += inputWidth + 5;
                            }
                            
                        }
                    }
                }
                else
                {
                    //Is a first party emote
                    int emoteId = Int32.Parse(fragment["emoticon"]["emoticon_id"].ToString());
                    Image emoteImage = null;

                    foreach (var emote in chatEmotes)
                    {
                        if (emoteId == emote.Key)
                            emoteImage = emote.Value;
                    }

                    if (emoteImage != null)
                    {
                        if (drawPos.X + emoteImage.Width > canvasSize.Width)
                            AddNewSection(ref messageSections, ref renderOptions, ref currentGifEmotes, ref currentSection, ref g, ref sectionImage, ref canvasSize, ref drawPos);
                        currentSection.hasEmote = true;
                        g.DrawImage(emoteImage, drawPos.X - 2, drawPos.Y - 6 + (28 - emoteImage.Height), emoteImage.Width, emoteImage.Height);
                        drawPos.X += emoteImage.Width - 2;
                    }
                }
            }

            messageSections.Add(currentSection);
            List<GifEmote> finalGifs = new List<GifEmote>();

            int finalY = 0;
            int finalHeight = 0;
            for (int i = 0; i < messageSections.Count; i++)
                finalHeight += messageSections[i].hasEmote ? 32 : 20;
            Bitmap final = new Bitmap(canvasSize.Width, finalHeight);
            g.Dispose();
            g = Graphics.FromImage(final);
            for (int i = 0; i < messageSections.Count; i++)
            {
                foreach (var gif in messageSections[i].gifEmotes)
                {
                    finalGifs.Add(new GifEmote(new Point(gif.offset.X, finalY), gif.name, gif.image));
                }

                if (messageSections[i].hasEmote)
                {
                    g.DrawImage(messageSections[i].section, 0, finalY);
                    finalY += 32;
                }
                else
                {
                    Bitmap temp = new Bitmap(renderOptions.chat_width, 20);
                    Graphics gtemp = Graphics.FromImage(temp);
                    gtemp.DrawImage(messageSections[i].section, 0, -6);
                    g.DrawImage(temp, 0, finalY);
                    temp.Dispose();
                    gtemp.Dispose();
                    finalY += 20;
                }
            }
            g.Dispose();
            currentSection.section.Dispose();
            finalComments.Add(new TwitchComment(final, Double.Parse(comment["content_offset_seconds"].ToString()), finalGifs));
        }

        private void AddNewSection(ref List<Section> messageSections, ref RenderOptions renderOptions, ref List<GifEmote> currentGifEmotes, ref Section currentSection, ref Graphics g, ref Bitmap bmp, ref Size canvasSize, ref Point drawPos)
        {
            messageSections.Add(currentSection);
            currentGifEmotes = new List<GifEmote>();
            bmp = new Bitmap(canvasSize.Width, canvasSize.Height);
            currentSection = new Section(bmp, false, currentGifEmotes);
            g = Graphics.FromImage(bmp);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.FillRectangle(new SolidBrush(renderOptions.background_color), 0, 0, canvasSize.Width, canvasSize.Height + 32);
            drawPos.X = 2;
        }

        private void GetEmotes(List<KeyValuePair<int, Image>> chatEmotes, JToken comments)
        {
            List<int> alreadyAdded = new List<int>();
            using (WebClient client = new WebClient())
            {
                foreach (var comment in comments)
                {
                    foreach (var fragment in comment["message"]["fragments"])
                    {
                        if (fragment["emoticon"] != null)
                        {
                            int id = fragment["emoticon"]["emoticon_id"].ToObject<int>();
                            if (!alreadyAdded.Contains(id))
                            {
                                alreadyAdded.Add(id);
                                byte[] bytes = client.DownloadData(String.Format("https://static-cdn.jtvnw.net/emoticons/v1/{0}/1.0", id));
                                MemoryStream ms = new MemoryStream(bytes);
                                Image emoteImage = System.Drawing.Image.FromStream(ms);
                                chatEmotes.Add(new KeyValuePair<int, Image>(id, emoteImage));
                            }
                        }
                    }
                }
            }
        }

        private void DrawUsername(Graphics g, Font nameFont, string userName, Color userColor, ref Size canvasSize, ref Point drawPos)
        {
            g.DrawString(userName + ":", nameFont, new SolidBrush(userColor), drawPos.X - 2, drawPos.Y + 2);
            drawPos.X += (int)Math.Floor(g.MeasureString(userName + ":", nameFont, 0, StringFormat.GenericTypographic).Width) + 6;
        }

        private void DrawBadges(Graphics g, List<ChatBadge> chatBadges, JToken comment, ref Size canvasSize, ref Point drawPos)
        {
            if (comment["message"]["user_badges"] != null)
            {
                foreach (var badge in comment["message"]["user_badges"])
                {
                    string id = badge["_id"].ToString();
                    string version = badge["version"].ToString();

                    Image badgeImage = null;
                    foreach (var cachedBadge in chatBadges)
                    {
                        if (cachedBadge.name == id)
                        {
                            foreach (var cachedVersion in cachedBadge.versions)
                            {
                                if (cachedVersion.Key == version)
                                {
                                    badgeImage = cachedVersion.Value;
                                }
                            }
                        }
                    }

                    if (badgeImage != null)
                    {
                        g.DrawImage(badgeImage, drawPos.X, drawPos.Y - 1, 18, 18);
                        drawPos.X += 20;
                    }
                }
            }
        }

        private void GetChatBadges(List<ChatBadge> chatBadges, JToken streamerInfo)
        {
            using (WebClient client = new WebClient())
            {
                //Global chat badges
                JObject globalBadges = JObject.Parse(client.DownloadString("https://badges.twitch.tv/v1/badges/global/display"));
                //Subscriber badges
                JObject subBadges = JObject.Parse(client.DownloadString(String.Format("https://badges.twitch.tv/v1/badges/channels/{0}/display", streamerInfo["id"].ToString())));

                foreach (var badge in globalBadges["badge_sets"].Union(subBadges["badge_sets"]))
                {
                    JProperty jBadgeProperty = badge.ToObject<JProperty>();
                    string name = jBadgeProperty.Name;
                    List<KeyValuePair<string, Image>> versions = new List<KeyValuePair<string, Image>>();

                    foreach (var version in badge.First["versions"])
                    {
                        JProperty jVersionProperty = version.ToObject<JProperty>();
                        string versionString = jVersionProperty.Name;
                        string downloadUrl = version.First["image_url_1x"].ToString();
                        byte[] bytes = client.DownloadData(downloadUrl);
                        MemoryStream ms = new MemoryStream(bytes);
                        try
                        {
                            //For some reason, twitch has corrupted images sometimes :) for example
                            //https://static-cdn.jtvnw.net/badges/v1/a9811799-dce3-475f-8feb-3745ad12b7ea/1
                            Image badgeImage = System.Drawing.Image.FromStream(ms);
                            versions.Add(new KeyValuePair<string, Image>(versionString, badgeImage));
                        }
                        catch (ArgumentException)
                        { }
                    }

                    chatBadges.Add(new ChatBadge(name, versions));
                }
            }
        }

        private void GetThirdPartyEmotes(List<ThirdPartyEmote> thirdPartyEmotes, RenderOptions renderOptions, JToken streamerInfo)
        {
            using (WebClient client = new WebClient())
            {
                if (renderOptions.bttv_emotes)
                {
                    //Global BTTV emotes
                    JObject BBTV = JObject.Parse(client.DownloadString("https://api.betterttv.net/2/emotes"));
                    foreach (var emote in BBTV["emotes"])
                    {
                        string id = emote["id"].ToString();
                        byte[] bytes = client.DownloadData(String.Format("https://cdn.betterttv.net/emote/{0}/1x", id));
                        MemoryStream ms = new MemoryStream(bytes);
                        Image temp_emote = System.Drawing.Image.FromStream(ms);
                        thirdPartyEmotes.Add(new ThirdPartyEmote(temp_emote, emote["code"].ToString(), emote["imageType"].ToString(), id));
                    }

                    //Channel specific BTTV emotes
                    try
                    {
                        JObject BBTV_channel = JObject.Parse(client.DownloadString("https://api.betterttv.net/2/channels/" + streamerInfo["name"].ToString()));
                        foreach (var emote in BBTV_channel["emotes"])
                        {
                            string id = emote["id"].ToString();
                            byte[] bytes = client.DownloadData(String.Format("https://cdn.betterttv.net/emote/{0}/1x", id));
                            MemoryStream ms = new MemoryStream(bytes);
                            Image temp_emote = System.Drawing.Image.FromStream(ms);
                            thirdPartyEmotes.Add(new ThirdPartyEmote(temp_emote, emote["code"].ToString(), emote["imageType"].ToString(), id));
                        }
                    }
                    catch { }
                }

                if (renderOptions.ffz_emotes)
                {
                    //Global FFZ emotes
                    JObject FFZ = JObject.Parse(client.DownloadString("https://api.betterttv.net/2/frankerfacez_emotes/global"));
                    foreach (var emote in FFZ["emotes"])
                    {
                        string id = emote["id"].ToString();
                        byte[] bytes = client.DownloadData(String.Format("https://cdn.betterttv.net/frankerfacez_emote/{0}/1", id));
                        MemoryStream ms = new MemoryStream(bytes);
                        Image temp_emote = System.Drawing.Image.FromStream(ms);
                        thirdPartyEmotes.Add(new ThirdPartyEmote(temp_emote, emote["code"].ToString(), emote["imageType"].ToString(), id));
                    }

                    //Channel specific FFZ emotes
                    try
                    {
                        JObject FFZ_channel = JObject.Parse(client.DownloadString("https://api.betterttv.net/2/frankerfacez_emotes/channels/" + streamerInfo["id"].ToString()));
                        foreach (var emote in FFZ_channel["emotes"])
                        {
                            string id = emote["id"].ToString();
                            byte[] bytes = client.DownloadData(String.Format("https://cdn.betterttv.net/frankerfacez_emote/{0}/1", id));
                            MemoryStream ms = new MemoryStream(bytes);
                            Image temp_emote = System.Drawing.Image.FromStream(ms);
                            thirdPartyEmotes.Add(new ThirdPartyEmote(temp_emote, emote["code"].ToString(), emote["imageType"].ToString(), id));
                        }
                    }
                    catch { }
                }
            }
        }
        public static Image ScaleImage(Image image, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / image.Width;
            var ratioY = (double)maxHeight / image.Height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(image.Width * ratio);
            var newHeight = (int)(image.Height * ratio);

            var newImage = new Bitmap(newWidth, newHeight);

            using (var graphics = Graphics.FromImage(newImage))
                graphics.DrawImage(image, 0, 0, newWidth, newHeight);

            return newImage;
        }
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON Files | *.json";
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                textJSON.Text = openFileDialog.FileName;
            }
        }

        private void TextColor_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Color color = ColorTranslator.FromHtml(textColor.Text);
                colorDialog.Color = color;
                btnColor.BackColor = color;
            }
            catch { }
        }

        private void BackgroundRenderManager_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            string message = (string)e.UserState;
            toolStatus.Text = message;
            toolProgressBar.Value = e.ProgressPercentage >= 100 ? 100 : e.ProgressPercentage;
        }

        private void BackgroundRenderManager_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            toolStatus.Text = "Done Rendering";
            toolProgressBar.Value = 0;
            btnRender.Enabled = true;
        }
    }
    
}

public class RenderOptions
{
    public string json_path { get; set; }
    public string save_path { get; set; }
    public Color background_color { get; set; }
    public int chat_height { get; set; }
    public int chat_width { get; set; }
    public bool bttv_emotes { get; set; }
    public bool ffz_emotes { get; set; }

    public RenderOptions(string Json_path, string Save_path, Color Background_color, int Chat_height, int Chat_width, bool Bttv_emotes, bool Ffz_emotes)
    {
        json_path = Json_path;
        save_path = Save_path;
        background_color = Background_color;
        chat_height = Chat_height;
        chat_width = Chat_width;
        bttv_emotes = Bttv_emotes;
        ffz_emotes = Ffz_emotes;
    }
}

public class ThirdPartyEmote
{
    public Image emote;
    public string imageType;
    public string name;
    public string id;
    public int width;
    public int height;
    public int fps = 0;
    public int frames = 0;

    public ThirdPartyEmote(Image Emote, string Name, string ImageType, string Id)
    {
        emote = Emote;
        name = Name;
        imageType = ImageType;
        id = Id;
        width = emote.Width;
        height = emote.Height;
        if (imageType == "gif")
        {
            FrameDimension dim = new FrameDimension(emote.FrameDimensionsList[0]);
            frames = emote.GetFrameCount(dim);
            PropertyItem item = emote.GetPropertyItem(0x5100);
            fps = (item.Value[0] + item.Value[1] * 256) * 10;

            if (fps == 0)
                fps = 2;
        }
    }
}

public class TwitchComment
{
    public Bitmap section;
    public double secondsOffset;
    public List<GifEmote> gifEmotes;

    public TwitchComment(Bitmap Section, double SecondsOffset, List<GifEmote> GifEmotes)
    {
        section = Section;
        secondsOffset = SecondsOffset;
        gifEmotes = GifEmotes;
    }
}

public class GifEmote
{
    public Point offset;
    public string name;
    public Image image;
    public FrameDimension dim;
    public int fps;
    public int frames;

    public GifEmote(Point Offset, string Name, Image Image)
    {
        offset = Offset;
        name = Name;
        image = Image;
        PropertyItem item = image.GetPropertyItem(0x5100);
        fps = (item.Value[0] + item.Value[1] * 256) * 10;
        if (fps == 0)
            fps = 2;
        dim = new FrameDimension(image.FrameDimensionsList[0]);
        frames = image.GetFrameCount(dim);
    }
}

public class ChatBadge
{
    public string name;
    public List<KeyValuePair<string, Image>> versions;

    public ChatBadge(string Name, List<KeyValuePair<string, Image>> Versions)
    {
        name = Name;
        versions = Versions;
    }
}

public class Section
{
    public Bitmap section;
    public bool hasEmote;
    public List<GifEmote> gifEmotes;

    public Section(Bitmap Section, bool HasEmote, List<GifEmote> GifEmotes)
    {
        section = Section;
        hasEmote = HasEmote;
        gifEmotes = GifEmotes;
    }
}