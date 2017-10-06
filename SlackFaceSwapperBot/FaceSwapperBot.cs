using Emgu.CV;
using Emgu.CV.Structure;
using Newtonsoft.Json;
using SlackAPI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace SlackFaceSwapperBot
{
    class FaceSwapperBot
    {
        [SlackSocketRouting("file_shared")]
        public class NewFileSharedMessage : SlackSocketMessage
        {
            public string file_id;
            public string user_id;
        }

        public class ConfigInfo
        {
            public string botToken;
            public string classifierFile;
            public double upScalingFactor;
        }

        private SlackSocketClient client;
        private CascadeClassifier haarCascade;
        private List<Channel> myChannels = new List<Channel>();
        private List<Bitmap> faceImages = new List<Bitmap>();
        private Random rand;
        private System.Timers.Timer checkSocketTimer;
        private ConfigInfo config;

        public static void Main(string[] args)
        {
            var bot = new FaceSwapperBot();
            bot.Start();
        }

        public void Start()
        {
            rand = new Random();
            LoadConfig();
            LoadFaces();

            haarCascade = new CascadeClassifier("Assets/" + config.classifierFile);

            client = new SlackSocketClient(config.botToken);

            checkSocketTimer = new System.Timers.Timer();
            checkSocketTimer.Elapsed += CheckSocketConnected;
            checkSocketTimer.Interval = 10000; // in miliseconds
            checkSocketTimer.Start();

            try
            {
                ManualResetEventSlim clientReady = new ManualResetEventSlim(false);
                ManualResetEventSlim stopClient = new ManualResetEventSlim(false);

                client.Connect((connected) =>
                {
                    // This is called once the client has emitted the RTM start command
                    clientReady.Set();
                    Console.WriteLine("Connected!");
                }, () =>
                {
                    // This is called once the RTM client has connected to the end point
                });

                clientReady.Wait();
                
                client.BindCallback<NewFileSharedMessage>(OnNewFileShared);
                client.GetChannelList(GetChannelsCallback);

                var c = client.Channels.Find(x => x.name.Equals("random")); //we listen and post only on the random channel
                myChannels.Add(c);

                stopClient.Wait();
            }
            catch(Exception e)
            {
                Console.WriteLine("Error: Unable to connect to slack.");
            }
            
        }

        private void OnNewFileShared(NewFileSharedMessage message)
        {
            if (message.user_id != client.MySelf.id)
            {
                client.GetFileInfo(GetFileInfoCallback, message.file_id); //Get uploaded file info, make our changes and upload it again
            }
        }

        private void LoadConfig()
        {
            using (StreamReader r = new StreamReader("config.ini"))
            {
                string json = r.ReadToEnd();
                config = JsonConvert.DeserializeObject<ConfigInfo>(json);
                Console.WriteLine("Config loaded.");
            }
        }

        private void LoadFaces()
        {
            int i = 1;

            while (System.IO.File.Exists("Assets/Images/face_" + i + ".png"))
            {
                var image = new Bitmap("Assets/Images/face_" + i + ".png");
                var mirroredImage = (Bitmap)image.Clone();
                mirroredImage.RotateFlip(RotateFlipType.RotateNoneFlipX);

                faceImages.Add(image);
                faceImages.Add(mirroredImage);

                i++;
            }
        }

        private void CheckSocketConnected(object sender, EventArgs ev)
        {
            if(!client.IsConnected)
            {
                Console.WriteLine("Not connected, attempting reconnect.");

                try
                {
                    ManualResetEventSlim clientReady = new ManualResetEventSlim(false);
                    client.Connect((connected) =>
                    {
                        // This is called once the client has emitted the RTM start command
                        clientReady.Set();
                        Console.WriteLine("Connected!");
                    }, () =>
                    {
                        // This is called once the RTM client has connected to the end point
                    });

                    clientReady.Wait();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to reconnect.");
                }
                
            }
        }

        private void GetChannelsCallback(ChannelListResponse clr)
        {
            if(clr.ok)
            {
                Console.WriteLine("Got channels.");
            }
            else
            {
                Console.WriteLine(clr.error);
            }
        }

        private void GetFileInfoCallback(FileInfoResponse fir)
        {
            if(fir.ok)
            {
                Console.WriteLine("Got File info.");

                if (fir.file.initial_comment.comment.Contains(client.MySelf.id) || fir.file.initial_comment.comment.Contains("faceswapperbot"))
                {
                    using (var webClient = new WebClient())
                    {
                        webClient.Headers.Add(HttpRequestHeader.Authorization, "Bearer " + config.botToken);
                        webClient.DownloadFile(fir.file.url_private, "srcFile." + fir.file.filetype);

                        var src = new Image<Bgr, byte>("srcFile." + fir.file.filetype);

                        var graySrc = src.Convert<Gray, byte>();    //Convert to gray scale
                        graySrc._EqualizeHist();                    //Equalize histogram for better detection
                                                                    //graySrc = graySrc.SmoothGaussian(25);     //Smooth the image with a gaussian filter
                        var faces = haarCascade.DetectMultiScale(graySrc);

                        var dest = src.ToBitmap();

                        //create graphics from main image
                        using (Graphics g = Graphics.FromImage(dest))
                        {
                            //Draw each new face on top of the old ones
                            foreach (var face in faces)
                            {
                                int faceIdx = rand.Next(faceImages.Count);
                                var newFace = faceImages[faceIdx];

                                var ratio = (float)newFace.Width / newFace.Height;
                                var h = face.Height;
                                var w = (int)(face.Height * ratio);
                                var difference = (face.Width - w) / 2f;

                                g.DrawImage(faceImages[faceIdx],
                                            face.X + difference - (w * (float)config.upScalingFactor / 2f),    //faces are fitted aacording to their height, so we need to center them according to width
                                            face.Y - (h * (float)config.upScalingFactor / 2f),                 //we also center them according to their up scaling
                                            w * (1f + (float)config.upScalingFactor),                          //we increase the width and height of the new faces according to their up scaling
                                            h * (1f + (float)config.upScalingFactor));
                            }

                            //Save modified image to memory stream and upload it
                            using (MemoryStream ms = new MemoryStream())
                            {
                                dest.Save(ms, ImageFormat.Png);

                                client.UploadFile(FileUploadCallback, ms.ToArray(), fir.file.id + "_swapped." + fir.file.filetype, myChannels.Select(c => c.id).ToArray());
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine(fir.error);
            }
            
        }

        private void FileUploadCallback(FileUploadResponse fur)
        {
            if (fur.ok)
            {
                Console.WriteLine("Uploaded File.");
            }
            else
            {
                Console.WriteLine(fur.error);
            }
        }
    }
}