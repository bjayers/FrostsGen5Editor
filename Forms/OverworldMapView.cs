using NewEditor.Data.NARCTypes;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    public class OverworldMapView : UserControl
    {
        public const int TilesPerCell = 32;

        OverworldEditor host;
        Panel canvasHost;
        PictureBox canvas;
        TrackBar zoomBar;
        Label infoLabel;
        CheckBox showWarpsBox;
        CheckBox showTriggersBox;

        int tilePx = 12;
        int mapW = 32;
        int mapH = 32;
        int[,] occupancy;
        int dragNpc = -1;
        bool dragging;
        Point lastTile;

        public OverworldMapView()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(32, 34, 38);

            var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(24, 26, 30) };
            infoLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.Gainsboro,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Text = "Select a zone to draw its matrix."
            };
            zoomBar = new TrackBar
            {
                Minimum = 6,
                Maximum = 24,
                Value = 12,
                TickFrequency = 2,
                Width = 120,
                Dock = DockStyle.Right
            };
            zoomBar.ValueChanged += (s, e) => { tilePx = zoomBar.Value; ResizeCanvas(); InvalidateMap(); };

            showTriggersBox = new CheckBox { Text = "Triggers", ForeColor = Color.Gainsboro, AutoSize = true, Checked = true, Dock = DockStyle.Right, Padding = new Padding(6, 8, 8, 0) };
            showWarpsBox = new CheckBox { Text = "Warps", ForeColor = Color.Gainsboro, AutoSize = true, Checked = true, Dock = DockStyle.Right, Padding = new Padding(6, 8, 8, 0) };
            showTriggersBox.CheckedChanged += (s, e) => InvalidateMap();
            showWarpsBox.CheckedChanged += (s, e) => InvalidateMap();

            top.Controls.Add(infoLabel);
            top.Controls.Add(zoomBar);
            top.Controls.Add(showTriggersBox);
            top.Controls.Add(showWarpsBox);

            canvasHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(18, 20, 24) };
            canvas = new PictureBox { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.FromArgb(18, 20, 24) };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvasHost.Controls.Add(canvas);

            Controls.Add(canvasHost);
            Controls.Add(top);
        }

        public void Attach(OverworldEditor editor)
        {
            host = editor;
        }

        public void Rebuild()
        {
            mapW = 32;
            mapH = 32;
            occupancy = null;

            if (host == null || host.CurrentZone == null)
            {
                infoLabel.Text = "Select a zone to draw its matrix.";
                ResizeCanvas();
                InvalidateMap();
                return;
            }

            var z = host.CurrentZone;
            MapMatrixEntry mx = host.CurrentMatrix;
            if (mx != null && mx.width > 0 && mx.height > 0)
            {
                mapW = Math.Max(1, mx.width) * TilesPerCell;
                mapH = Math.Max(1, mx.height) * TilesPerCell;
                occupancy = mx.mapFiles;
            }

            int npcCount = host.CurrentObjects != null ? host.CurrentObjects.NPCs.Count : 0;
            infoLabel.Text = string.Format("{0}  •  {1}x{2} tiles  •  {3} NPC{4}  •  drag to move, click to select",
                z.ToString(), mapW, mapH, npcCount, npcCount == 1 ? "" : "s");
            ResizeCanvas();
            InvalidateMap();
        }

        void ResizeCanvas()
        {
            canvas.Size = new Size(Math.Max(32, mapW * tilePx + 1), Math.Max(32, mapH * tilePx + 1));
        }

        public void InvalidateMap()
        {
            canvas.Invalidate();
        }

        Point TileFromMouse(MouseEventArgs e)
        {
            int x = e.X / Math.Max(1, tilePx);
            int y = e.Y / Math.Max(1, tilePx);
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x >= mapW) x = mapW - 1;
            if (y >= mapH) y = mapH - 1;
            return new Point(x, y);
        }

        int HitNpc(Point tile)
        {
            var objs = host == null ? null : host.CurrentObjects;
            if (objs == null || objs.NPCs == null) return -1;
            int found = -1;
            for (int i = 0; i < objs.NPCs.Count; i++)
            {
                var n = objs.NPCs[i];
                if (n.xPosition == tile.X && n.yPosition == tile.Y) found = i;
            }
            return found;
        }

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (host == null || host.CurrentObjects == null) return;
            Point t = TileFromMouse(e);
            lastTile = t;
            int hit = HitNpc(t);
            if (hit >= 0)
            {
                host.SelectNpc(hit);
                dragNpc = hit;
                dragging = true;
                canvas.Capture = true;
            }
            else
            {
                dragNpc = -1;
                dragging = false;
            }
            InvalidateMap();
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point t = TileFromMouse(e);
            if (!dragging || dragNpc < 0 || host == null || host.CurrentObjects == null) return;
            if (t == lastTile) return;
            lastTile = t;
            host.MoveNpcTo(dragNpc, (short)t.X, (short)t.Y, persist: false);
            InvalidateMap();
        }

        void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (dragging && dragNpc >= 0 && host != null)
                host.MoveNpcTo(dragNpc, (short)lastTile.X, (short)lastTile.Y, persist: true);
            dragging = false;
            dragNpc = -1;
            canvas.Capture = false;
            InvalidateMap();
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Color.FromArgb(18, 20, 24));

            int cellsX = occupancy != null ? occupancy.GetLength(0) : 1;
            int cellsY = occupancy != null ? occupancy.GetLength(1) : 1;
            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int fileId = occupancy != null ? occupancy[cx, cy] : 0;
                    bool empty = fileId < 0;
                    Color fill = empty ? Color.FromArgb(22, 24, 28) : ColorFromCell(cx + cy * 7 + fileId);
                    using (var b = new SolidBrush(fill))
                        g.FillRectangle(b, cx * TilesPerCell * tilePx, cy * TilesPerCell * tilePx, TilesPerCell * tilePx, TilesPerCell * tilePx);
                    using (var p = new Pen(Color.FromArgb(60, 70, 80)))
                        g.DrawRectangle(p, cx * TilesPerCell * tilePx, cy * TilesPerCell * tilePx, TilesPerCell * tilePx, TilesPerCell * tilePx);
                }
            }

            using (var p = new Pen(Color.FromArgb(28, 40, 50)))
            {
                for (int x = 0; x <= mapW; x++) g.DrawLine(p, x * tilePx, 0, x * tilePx, mapH * tilePx);
                for (int y = 0; y <= mapH; y++) g.DrawLine(p, 0, y * tilePx, mapW * tilePx, y * tilePx);
            }

            var objs = host == null ? null : host.CurrentObjects;
            if (objs == null) return;

            if (showWarpsBox.Checked && objs.warps != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(180, 80, 200)))
                {
                    foreach (var w in objs.warps)
                    {
                        float wx = w.rail ? w.exitX : (w.exitX - 8) / 16f;
                        float wy = w.rail ? w.exitY : w.exitY / 16f;
                        float px = wx * tilePx;
                        float py = wy * tilePx;
                        PointF[] diamond =
                        {
                            new PointF(px + tilePx / 2f, py + 1),
                            new PointF(px + tilePx - 1, py + tilePx / 2f),
                            new PointF(px + tilePx / 2f, py + tilePx - 1),
                            new PointF(px + 1, py + tilePx / 2f)
                        };
                        g.FillPolygon(b, diamond);
                    }
                }
            }

            if (showTriggersBox.Checked && objs.triggers != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(70, 220, 180, 40)))
                using (var pen = new Pen(Color.FromArgb(220, 180, 40)))
                {
                    foreach (var t in objs.triggers)
                    {
                        int tw = Math.Max(1, (int)t.width);
                        int th = Math.Max(1, (int)t.height);
                        g.FillRectangle(b, t.xPosition * tilePx, t.yPosition * tilePx, tw * tilePx, th * tilePx);
                        g.DrawRectangle(pen, t.xPosition * tilePx, t.yPosition * tilePx, tw * tilePx, th * tilePx);
                    }
                }
            }

            if (objs.NPCs == null) return;
            int selected = host.SelectedNpcIndex;
            using (var font = new Font("Segoe UI", Math.Max(6f, tilePx * 0.55f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < objs.NPCs.Count; i++)
                {
                    var n = objs.NPCs[i];
                    int px = n.xPosition * tilePx;
                    int py = n.yPosition * tilePx;
                    bool sel = i == selected;
                    Color body = SpriteColor(n.sprite);
                    using (var b = new SolidBrush(sel ? Color.FromArgb(255, 230, 80) : body))
                    using (var pen = new Pen(sel ? Color.White : Color.FromArgb(20, 20, 20), sel ? 2 : 1))
                    {
                        Rectangle r = new Rectangle(px + 1, py + 1, Math.Max(4, tilePx - 2), Math.Max(4, tilePx - 2));
                        g.FillEllipse(b, r);
                        g.DrawEllipse(pen, r);
                    }
                    using (var tb = new SolidBrush(sel ? Color.Black : Color.White))
                        g.DrawString(i.ToString(), font, tb, new RectangleF(px, py, tilePx, tilePx), sf);
                }
            }

            if (selected >= 0 && selected < objs.NPCs.Count)
            {
                var n = objs.NPCs[selected];
                string badge = string.Format("#{0}  spr {1}  ({2},{3})", selected, n.sprite, n.xPosition, n.yPosition);
                using (var font = new Font("Segoe UI", 8f))
                using (var bg = new SolidBrush(Color.FromArgb(200, 10, 10, 12)))
                using (var fg = new SolidBrush(Color.White))
                {
                    SizeF sz = g.MeasureString(badge, font);
                    g.FillRectangle(bg, 4, 4, sz.Width + 8, sz.Height + 4);
                    g.DrawString(badge, font, fg, 8, 6);
                }
            }
        }

        static Color ColorFromCell(int seed)
        {
            return FromHsv(Math.Abs(seed * 47) % 360, 0.25f, 0.32f);
        }

        static Color SpriteColor(int sprite)
        {
            return FromHsv(Math.Abs(sprite * 37 + 80) % 360, 0.55f, 0.75f);
        }

        static Color FromHsv(int h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = v - c;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}
