using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chip8WindowsForms
{
    public sealed class GamePictureBox : PictureBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half; // ขอบคม

            g.Clear(BackColor);

            Rectangle dest = new Rectangle(0, 0, Width, Height);
            Rectangle src = new Rectangle(0, 0, Image.Width, Image.Height);
            g.DrawImage(Image, dest, src, GraphicsUnit.Pixel);

        }
    }
}
