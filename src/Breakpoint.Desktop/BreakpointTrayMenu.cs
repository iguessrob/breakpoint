using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms=System.Windows.Forms;

namespace Breakpoint.Desktop;

public sealed class BreakpointTrayMenu : Forms.ContextMenuStrip
{
    private readonly Forms.ToolStripMenuItem visibilityItem;
    private readonly Func<bool> islandVisible;
    private readonly MenuRenderer appearance=new();
    private sealed record Entry(string Glyph,bool Pin=false);

    public BreakpointTrayMenu(bool preview,Func<bool> visible,Action open,Action toggle,Action skip,Action notification,Action warning,Action quit)
    {
        islandVisible=visible;Renderer=appearance;ShowImageMargin=false;ShowCheckMargin=false;
        Font=new Font("Segoe UI",10);Padding=new Forms.Padding(7);AutoSize=false;Width=280;
        Items.Add(new Forms.ToolStripLabel(preview?"Preview mode":"Curfew controls") { Tag="header",AutoSize=false,Size=new Size(264,52) });
        Add("Open settings","\uE713",open);
        visibilityItem=Add("Hide island","\uE890",toggle);
        Add("Skip this curfew","\uE893",skip,true);
        if(preview)
        {
            Separator();Add("Preview notification","\uE7F4",notification);Add("Test shutdown warning","\uE823",warning,true);
        }
        Separator();Add("Quit interface","\uE8BB",quit,true);
        Height=Padding.Vertical;foreach(Forms.ToolStripItem item in Items)Height+=item.Height+item.Margin.Vertical;
        Opening+=(_,_)=>RefreshAppearance();
        Disposed+=(_,_)=>{appearance.Dispose();Font.Dispose();};
    }
    public void RefreshAppearance()
    {
        visibilityItem.Text=islandVisible()?"Hide island":"Show island";
        BackColor=appearance.Background;ForeColor=appearance.Foreground;
        using var shape=MenuRenderer.Round(new Rectangle(0,0,Width,Height),10);
        Region?.Dispose();Region=new Region(shape);Invalidate();
    }
    private Forms.ToolStripMenuItem Add(string text,string glyph,Action action,bool pin=false)
    {
        var item=new Forms.ToolStripMenuItem(text) { Tag=new Entry(glyph,pin),AutoSize=false,Size=new Size(264,38),Margin=new Forms.Padding(0,1,0,1),ToolTipText=pin?"Owner PIN required":"" };
        item.Click+=(_,_)=>action();Items.Add(item);return item;
    }
    private void Separator()=>Items.Add(new Forms.ToolStripSeparator { AutoSize=false,Size=new Size(264,12),Margin=new Forms.Padding(0) });

    private sealed class MenuRenderer : Forms.ToolStripRenderer,IDisposable
    {
        private readonly Font glyph=new("Segoe MDL2 Assets",11),heading=new("Segoe UI",10,FontStyle.Bold),caption=new("Segoe UI",8.5f),pinFont=new("Segoe UI",8);
        private readonly Bitmap logo;
        public MenuRenderer(){using var icon=BrandAssets.TrayIcon();logo=icon.ToBitmap();}
        private bool Contrast=>Forms.SystemInformation.HighContrast;
        private bool Light=>App.CurrentTheme=="Light";
        public Color Background=>Contrast?SystemColors.Menu:ColorTranslator.FromHtml(Light?"#FFFFFF":"#1B1D20");
        public Color Foreground=>Contrast?SystemColors.MenuText:ColorTranslator.FromHtml(Light?"#192027":"#F1F2F3");
        private Color Dim=>Contrast?SystemColors.MenuText:ColorTranslator.FromHtml(Light?"#515D69":"#BEC5CE");
        private Color Line=>Contrast?SystemColors.MenuText:ColorTranslator.FromHtml(Light?"#D6DCE2":"#3A4047");
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e){e.Graphics.Clear(Background);}
        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var path=Round(new Rectangle(0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1),10);using var pen=new Pen(Line);e.Graphics.DrawPath(pen,path);
        }
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Selected)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using var path=Round(new Rectangle(0,0,e.Item.Width-1,e.Item.Height-1),7);
            using var brush=new SolidBrush(Contrast?SystemColors.Highlight:ColorTranslator.FromHtml(Light?"#F0F3F5":"#292D31"));e.Graphics.FillPath(brush,path);
        }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            var g=e.Graphics;var flags=Forms.TextFormatFlags.Left|Forms.TextFormatFlags.VerticalCenter|Forms.TextFormatFlags.NoPrefix|Forms.TextFormatFlags.NoPadding;
            if(e.Item.Tag is string)
            {
                using var imageStyle=new System.Drawing.Imaging.ImageAttributes();
                if(Light||Contrast)
                {
                    var tint=Contrast?Foreground:ColorTranslator.FromHtml("#275E46");
                    imageStyle.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[] {
                        new float[] {0,0,0,0,0},new float[] {0,0,0,0,0},new float[] {0,0,0,0,0},
                        new float[] {0,0,0,1,0},new float[] {tint.R/255f,tint.G/255f,tint.B/255f,0,1}
                    }));
                }
                g.DrawImage(logo,new Rectangle(10,10,16,16),0,0,logo.Width,logo.Height,GraphicsUnit.Pixel,imageStyle);
                Forms.TextRenderer.DrawText(g,"breakpoint",heading,new Rectangle(36,6,210,24),Foreground,flags);
                Forms.TextRenderer.DrawText(g,e.Item.Text,caption,new Rectangle(36,28,210,18),Dim,flags);return;
            }
            if(e.Item.Tag is not Entry entry){base.OnRenderItemText(e);return;}
            var ink=Contrast&&e.Item.Selected?SystemColors.HighlightText:Foreground;
            if(entry.Glyph=="\uE7F4")
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using var pen=new Pen(ink,1.3f);using var bell=new GraphicsPath();var top=e.Item.Height/2-7;
                bell.AddBezier(13,top+8,13,top-3,25,top-3,25,top+8);bell.AddLine(25,top+8,27,top+12);bell.AddLine(27,top+12,11,top+12);bell.AddLine(11,top+12,13,top+8);g.DrawPath(pen,bell);g.DrawArc(pen,17,top+12,4,4,0,180);
            }
            else Forms.TextRenderer.DrawText(g,entry.Glyph,glyph,new Rectangle(11,0,20,e.Item.Height),ink,flags);
            Forms.TextRenderer.DrawText(g,e.Item.Text,e.TextFont,new Rectangle(42,0,e.Item.Width-42-(entry.Pin?38:8),e.Item.Height),ink,flags);
            if(entry.Pin)Forms.TextRenderer.DrawText(g,"PIN",pinFont,new Rectangle(e.Item.Width-31,0,28,e.Item.Height),Contrast?ink:Dim,flags);
        }
        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e){using var pen=new Pen(Line);e.Graphics.DrawLine(pen,10,e.Item.Height/2,e.Item.Width-10,e.Item.Height/2);}
        internal static GraphicsPath Round(Rectangle r,int radius)
        {
            var path=new GraphicsPath();var d=radius*2;path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public void Dispose(){glyph.Dispose();heading.Dispose();caption.Dispose();pinFont.Dispose();logo.Dispose();}
    }
}
