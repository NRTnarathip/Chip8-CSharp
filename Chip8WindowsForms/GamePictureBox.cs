using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Chip8WindowsForms
{
    public sealed class GamePictureBox : PictureBox
    {
        public InterpolationMode interpolationMode { get; set; } = InterpolationMode.NearestNeighbor;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = interpolationMode;

            base.OnPaint(e);
        }
    }
}
