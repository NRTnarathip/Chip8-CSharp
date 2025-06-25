using Chip8;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Chip8WindowsForms
{
    public partial class MainForm : Form
    {
        Bitmap screen;
        PictureBox pictureBox;

        readonly CPU cpu = new();

        public MainForm()
        {
            InitializeComponent();

            screen = new(CPU.ScreenWidth, CPU.ScreenHeight);
            int zoomScale = 15;
            pictureBox = new PictureBox
            {
                Width = screen.Width * zoomScale,
                Height = screen.Height * zoomScale,
                BorderStyle = BorderStyle.FixedSingle,
                Image = screen,

                SizeMode = PictureBoxSizeMode.Zoom,
            };
            this.Controls.Add(pictureBox);
            ClientSize = new(pictureBox.Width, pictureBox.Height);

            KeyPreview = true;
            KeyDown += Form_KeyDown;
            KeyUp += Form_KeyUp;

            // ready
            Task.Run(MainLoop);
        }

        void SetKeyState(Keys key, bool isKeyDown)
        {
            var keyHex = GetKeyHexFromKeyCode(key);
            if (keyHex != 0xFF)
            {
                if (cpu.UpdateKeyState(keyHex, isKeyDown))
                {
                    Console.WriteLine("new update key: " + key);
                }
            }
        }

        byte GetKeyHexFromKeyCode(Keys key)
        {
            switch (key)
            {
                case Keys.D0:
                    return 0;
                case Keys.D1:
                    return 1;
                case Keys.D2:
                    return 2;
                case Keys.D3:
                    return 3;
                case Keys.D4:
                    return 4;
                case Keys.D5:
                    return 5;
                case Keys.D6:
                    return 6;
                case Keys.D7:
                    return 7;
                case Keys.D8:
                    return 8;
                case Keys.D9:
                    return 9;
                case Keys.A:
                    return 0xA;
                case Keys.B:
                    return 0xB;
                case Keys.C:
                    return 0xC;
                case Keys.D:
                    return 0xD;
                case Keys.E:
                    return 0xE;
                case Keys.F:
                    return 0xF;

                case Keys.Left:
                    return 0x4;
                case Keys.Right:
                    return 0x6;
                case Keys.Up:
                    return 0x2;
                case Keys.Down:
                    return 0x8;

                // not found
                default:
                    return 0xFF;
            }
        }

        private void Form_KeyUp(object? sender, KeyEventArgs e)
        {
            SetKeyState(e.KeyCode, false);
        }

        private void Form_KeyDown(object? sender, KeyEventArgs e)
        {
            SetKeyState(e.KeyCode, true);
        }

        private unsafe void MainLoop()
        {
            Console.WriteLine("start main loop");

            bool romLoaded = cpu.LoadRom(
                @"C:\Users\narat\Documents\Gameboy Learn\chip8\br8kout.ch8");

            if (romLoaded is false)
                return;

            var tick60HZTimer = new System.Diagnostics.Stopwatch();
            tick60HZTimer.Restart();

            var gameTimer = new System.Diagnostics.Stopwatch();
            gameTimer.Restart();

            const float Tick60HZMs = 1000f / 60;

            while (true)
            {
                cpu.Cycle();

                // tick60hz
                var tickAcc = tick60HZTimer.Elapsed.TotalMicroseconds;
                if (tickAcc >= Tick60HZMs)
                {
                    if (cpu.DelayTime > 0)
                        cpu.DelayTime--;
                    //Console.WriteLine("tick acc: " + tickAcc);
                    Render();

                    tick60HZTimer.Restart();
                }

            }
        }

        void Render()
        {
            if (cpu.DisplayNeedRedraw is false)
                return;

            cpu.DisplayNeedRedraw = false;
            pictureBox.Invoke(() =>
              {
                  var bits = screen.LockBits(
                      new Rectangle(0, 0, screen.Width, screen.Height),
                      ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                  var display = cpu.Display;
                  unsafe
                  {
                      byte* pixels = (byte*)bits.Scan0;

                      for (var y = 0; y < screen.Height; y++)
                      {
                          for (var x = 0; x < screen.Width; x++)
                          {
                              pixels[0] = 0; // Blue
                              pixels[1] = display[x, y] ? (byte)0x64 : (byte)0; // Green
                              pixels[2] = 0; // Red
                              pixels[3] = 255; // Alpha
                              pixels += 4; // 4 bytes per pixel
                          }
                      }
                  }

                  screen.UnlockBits(bits);

                  pictureBox.Refresh();
              });

        }

        private void MainForm_Load(object sender, EventArgs e)
        {

        }
    }
}
