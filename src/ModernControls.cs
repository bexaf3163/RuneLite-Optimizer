using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RuneLiteOptimizer
{
    public static class Geometry
    {
        public static GraphicsPath Round(RectangleF bounds,float radius)
        {
            float d=radius*2;var p=new GraphicsPath();p.AddArc(bounds.X,bounds.Y,d,d,180,90);p.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);p.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
    }
    public sealed class ModernButton : Button
    {
        bool hover;
        public ModernButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.FromArgb(11,14,19):Parent.BackColor);
            Color fill=hover?ControlPaint.Light(BackColor,.12f):BackColor;
            using(var path=Geometry.Round(new RectangleF(1,1,Width-2,Height-2),9))using(var brush=new SolidBrush(fill)){e.Graphics.FillPath(brush,path);if(Focused)using(var pen=new Pen(Color.FromArgb(146,172,255)))e.Graphics.DrawPath(pen,path);}
            bool icon=Text.Length<=2;TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(icon?0:10,0,icon?Width:Width-20,Height),Enabled?ForeColor:Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|(icon?TextFormatFlags.NoPadding:TextFormatFlags.EndEllipsis));
        }
    }
    public sealed class ModernComboBox : ComboBox
    {
        void Chrome(Graphics graphics)
        {
            using(var fill=new SolidBrush(BackColor))graphics.FillRectangle(fill,Width-24,0,24,Height);
            using(var edge=new Pen(BackColor,2))graphics.DrawRectangle(edge,1,1,Math.Max(1,Width-27),Math.Max(1,Height-3));
            using(var border=new Pen(Focused?Color.FromArgb(146,172,255):Color.FromArgb(46,55,70)))graphics.DrawRectangle(border,0,0,Width-1,Height-1);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(160,171,189),1.4f))graphics.DrawLines(pen,new[]{new PointF(Width-17,Height/2f-2),new PointF(Width-13,Height/2f+2),new PointF(Width-9,Height/2f-2)});
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if((message.Msg==0x317 || message.Msg==0x318) && message.WParam!=IntPtr.Zero){using(var graphics=Graphics.FromHdc(message.WParam))Chrome(graphics);}
            else if(message.Msg==0xF || message.Msg==0x85){using(var graphics=Graphics.FromHwnd(Handle))Chrome(graphics);}
        }
    }
}
