using Chip8;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Chip8WindowsForms
{
    public partial class MainForm : Form
    {
        Bitmap screenBitmap;
        GamePictureBox screenPictureBox;

        readonly CPU cpu = new();

        public MainForm()
        {
            InitializeComponent();

            screenBitmap = new(CPU.ScreenWidth, CPU.ScreenHeight);
            int zoomScale = 15;
            screenPictureBox = new()
            {
                Width = screenBitmap.Width * zoomScale,
                Height = screenBitmap.Height * zoomScale,
                BorderStyle = BorderStyle.FixedSingle,
                Image = screenBitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            this.Controls.Add(screenPictureBox);
            ClientSize = new(screenPictureBox.Width, screenPictureBox.Height);

            KeyPreview = true;
            KeyDown += Form_KeyDown;
            KeyUp += Form_KeyUp;

            // ready
            Task.Run(StartGameThread);
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

        private unsafe void StartGameThread()
        {
            Console.WriteLine("start main loop");

            var romDir = @"C:\Users\narat\Documents\Gameboy Learn\chip8\Roms";
            var romFileName = "test_opcode.ch8";
            //var romFileName = "br8kout.ch8";
            bool romLoaded = cpu.LoadRom(Path.Combine(romDir, romFileName));

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
                var timerMs = tick60HZTimer.Elapsed.TotalMilliseconds;
                if (timerMs >= Tick60HZMs)
                {
                    cpu.OnTick60HZ();
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
            screenPictureBox.Invoke(() =>
              {
                  var bits = screenBitmap.LockBits(
                      new Rectangle(0, 0, screenBitmap.Width, screenBitmap.Height),
                      ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                  var display = cpu.Display;
                  unsafe
                  {
                      byte* pixels = (byte*)bits.Scan0;

                      for (var y = 0; y < screenBitmap.Height; y++)
                      {
                          for (var x = 0; x < screenBitmap.Width; x++)
                          {
                              pixels[0] = 0; // Blue
                              pixels[1] = display[x, y] ? (byte)0x64 : (byte)0; // Green
                              pixels[2] = 0; // Red
                              pixels[3] = 255; // Alpha
                              pixels += 4; // 4 bytes per pixel
                          }
                      }
                  }

                  screenBitmap.UnlockBits(bits);

                  screenPictureBox.Refresh();
              });
        }

        private void MainForm_Load(object sender, EventArgs e)
        {

        }
    }
}
