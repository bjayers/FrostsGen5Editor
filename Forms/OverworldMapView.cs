using NewEditor.Data.NARCTypes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    /// <summary>
    /// Primitive tile map for the overworld editor.
    /// Uses the zone matrix (32 tiles per cell) and live NPC coordinates from the open ROM.
    /// </summary>
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
        CheckBox showProxiesBox;
        CheckBox fitZoneBox;
        CheckBox showTerrainBox;
        CheckBox allTilesBox;
        CheckBox showLeashBox;
        CheckBox showSightBox;
        CheckBox editPermsBox;
        NumericUpDown floorBox;
        ToolTip hoverTip;

        int tilePx = 12;
        int mapW = 32;
        int mapH = 32;
        int originX; // global tile origin of the drawn crop
        int originY;
        int[,] occupancy; // matrix cell file id
        int[,] headers;   // matrix cell header/zone id, may be null
        Dictionary<int, byte[,]> perms = new Dictionary<int, byte[,]>();
        int dragNpc = -1;
        int dragProxy = -1;
        bool dragging;
        bool panning;
        bool paintingPerms;
        byte paintValue;
        Point panLast;
        Point dragStartPixel;
        Point lastTile;
        Point hoverTile = new Point(-1, -1);
        bool suppressZoomEvent;

        public OverworldMapView()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(32, 34, 38);

            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.FromArgb(24, 26, 30) };
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
                Maximum = 28,
                Value = 12,
                TickFrequency = 2,
                Width = 110,
                Dock = DockStyle.Right
            };
            zoomBar.ValueChanged += ZoomBarChanged;

            showTriggersBox = MakeToggle("Triggers", true);
            showWarpsBox = MakeToggle("Warps", true);
            showProxiesBox = MakeToggle("Proxies", true);
            fitZoneBox = MakeToggle("Fit zone", true);
            showTerrainBox = MakeToggle("Terrain", true);
            allTilesBox = MakeToggle("All tiles", false);
            showLeashBox = MakeToggle("Leash", true);
            showSightBox = MakeToggle("Sight", true);
            editPermsBox = MakeToggle("Edit perms", false);
            floorBox = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 3,
                Value = 0,
                Width = 42,
                Dock = DockStyle.Right
            };
            var floorLabel = new Label
            {
                Text = "Floor",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Dock = DockStyle.Right,
                Padding = new Padding(8, 12, 2, 0)
            };
            floorBox.ValueChanged += (s, e) => Rebuild();
            showTriggersBox.CheckedChanged += (s, e) => InvalidateMap();
            showWarpsBox.CheckedChanged += (s, e) => InvalidateMap();
            showProxiesBox.CheckedChanged += (s, e) => InvalidateMap();
            showTerrainBox.CheckedChanged += (s, e) => InvalidateMap();
            allTilesBox.CheckedChanged += (s, e) => InvalidateMap();
            showLeashBox.CheckedChanged += (s, e) => InvalidateMap();
            showSightBox.CheckedChanged += (s, e) => InvalidateMap();
            editPermsBox.CheckedChanged += (s, e) =>
            {
                canvas.Cursor = editPermsBox.Checked ? Cursors.Cross : Cursors.Default;
                InvalidateMap();
            };
            fitZoneBox.CheckedChanged += (s, e) => Rebuild();

            top.Controls.Add(infoLabel);
            top.Controls.Add(zoomBar);
            top.Controls.Add(editPermsBox);
            top.Controls.Add(floorBox);
            top.Controls.Add(floorLabel);
            top.Controls.Add(showTriggersBox);
            top.Controls.Add(showWarpsBox);
            top.Controls.Add(showProxiesBox);
            top.Controls.Add(showSightBox);
            top.Controls.Add(showLeashBox);
            top.Controls.Add(allTilesBox);
            top.Controls.Add(showTerrainBox);
            top.Controls.Add(fitZoneBox);

            // Custom panel: default AutoScroll Panel calls ScrollToControl when a child
            // (the PictureBox) takes focus, which jumps the map and then the still-held
            // mouse lands on a different tile — the "click an NPC and it teleports" bug.
            canvasHost = new MapScrollPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 20, 24), TabStop = false };
            canvas = new NoFocusBox { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.FromArgb(18, 20, 24), TabStop = false };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseLeave += (s, e) => { hoverTile = new Point(-1, -1); hoverTip.SetToolTip(canvas, ""); };
            canvasHost.MouseWheel += Canvas_MouseWheel;
            canvasHost.Controls.Add(canvas);

            hoverTip = new ToolTip { ShowAlways = true, AutoPopDelay = 12000, InitialDelay = 200 };

            Controls.Add(canvasHost);
            Controls.Add(top);
        }

        static CheckBox MakeToggle(string text, bool on)
        {
            return new CheckBox
            {
                Text = text,
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Checked = on,
                Dock = DockStyle.Right,
                Padding = new Padding(6, 10, 8, 0)
            };
        }

        public void Attach(OverworldEditor editor)
        {
            host = editor;
        }

        public void Rebuild()
        {
            mapW = 32;
            mapH = 32;
            originX = 0;
            originY = 0;
            occupancy = null;
            headers = null;

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
                occupancy = mx.mapFiles;
                headers = mx.mapHeaders;
                int cellsX = mx.width;
                int cellsY = mx.height;
                int minCx = 0, minCy = 0, maxCx = cellsX - 1, maxCy = cellsY - 1;

                // Fit zone crops to this zone's matrix headers. "All tiles" only
                // changes how blocked / empty tiles are painted — it does not
                // invent geometry outside the 32×32 permission grids.
                bool cropToZone = fitZoneBox.Checked;
                if (cropToZone && headers != null && headers.GetLength(0) == cellsX && headers.GetLength(1) == cellsY)
                {
                    int foundMinX = int.MaxValue, foundMinY = int.MaxValue, foundMaxX = -1, foundMaxY = -1;
                    for (int cy = 0; cy < cellsY; cy++)
                    {
                        for (int cx = 0; cx < cellsX; cx++)
                        {
                            if (headers[cx, cy] != z.index) continue;
                            if (cx < foundMinX) foundMinX = cx;
                            if (cy < foundMinY) foundMinY = cy;
                            if (cx > foundMaxX) foundMaxX = cx;
                            if (cy > foundMaxY) foundMaxY = cy;
                        }
                    }
                    if (foundMaxX >= 0)
                    {
                        minCx = foundMinX;
                        minCy = foundMinY;
                        maxCx = foundMaxX;
                        maxCy = foundMaxY;
                    }
                }

                originX = minCx * TilesPerCell;
                originY = minCy * TilesPerCell;
                mapW = Math.Max(1, (maxCx - minCx + 1) * TilesPerCell);
                mapH = Math.Max(1, (maxCy - minCy + 1) * TilesPerCell);
            }

            LoadPermissions();

            int npcCount = host.CurrentObjects != null && host.CurrentObjects.NPCs != null ? host.CurrentObjects.NPCs.Count : 0;
            int proxyCount = host.CurrentObjects != null && host.CurrentObjects.furniture != null ? host.CurrentObjects.furniture.Count : 0;
            int permCells = perms.Count;
            infoLabel.Text = string.Format("{0}  •  {1}×{2} @ ({3},{4})  •  {5} NPC{6}  •  {7} prox{8}  •  {9} map file{10}  •  Ctrl+click places a proxy",
                z.ToString(), mapW, mapH, originX, originY, npcCount, npcCount == 1 ? "" : "s",
                proxyCount, proxyCount == 1 ? "y" : "ies",
                permCells, permCells == 1 ? "" : "s");
            ResizeCanvas();
            InvalidateMap();
        }


        int ResolveFile(int fileId)
        {
            return fileId;
        }

        int PermBlock { get { return floorBox == null ? 0 : (int)floorBox.Value; } }

        void LoadPermissions()
        {
            perms.Clear();
            var narc = host == null ? null : host.MapFiles;
            if (narc == null || occupancy == null) return;
            int cellsX = occupancy.GetLength(0);
            int cellsY = occupancy.GetLength(1);
            int minCx = originX / TilesPerCell;
            int minCy = originY / TilesPerCell;
            int maxCx = Math.Min(cellsX - 1, (originX + mapW - 1) / TilesPerCell);
            int maxCy = Math.Min(cellsY - 1, (originY + mapH - 1) / TilesPerCell);
            for (int cy = Math.Max(0, minCy); cy <= maxCy; cy++)
            {
                for (int cx = Math.Max(0, minCx); cx <= maxCx; cx++)
                {
                    int fileId = ResolveFile(occupancy[cx, cy]);
                    if (fileId < 0 || perms.ContainsKey(fileId)) continue;
                    byte[,] grid = MapPermissionReader.GetTypes(narc, fileId, PermBlock);
                    if (grid != null) perms[fileId] = grid;
                }
            }
        }

        void PaintPermission(Point global, byte value)
        {
            if (occupancy == null || host == null || host.MapFiles == null) return;
            int cx = global.X / TilesPerCell;
            int cy = global.Y / TilesPerCell;
            if (cx < 0 || cy < 0 || cx >= occupancy.GetLength(0) || cy >= occupancy.GetLength(1)) return;
            int fileId = ResolveFile(occupancy[cx, cy]);
            if (fileId < 0) return;
            int lx = global.X - cx * TilesPerCell;
            int ly = global.Y - cy * TilesPerCell;
            int current = TileType(global.X, global.Y);
            // Only swap blocked <-> walk. Grass, water, ledges, snow stay put
            // unless the tile is already a collision type we own.
            bool blocked = current < 0 || MapPermissionReader.IsBlocked(current);
            bool walk = current == MapPermissionReader.TypeWalk;
            if (value == MapPermissionReader.TypeWalk)
            {
                if (!blocked) return;
            }
            else
            {
                if (!walk && current != -1) return;
            }
            if (!MapPermissionReader.SetType(host.MapFiles, fileId, lx, ly, value, PermBlock)) return;
            byte[,] grid;
            if (perms.TryGetValue(fileId, out grid) && grid != null &&
                lx >= 0 && ly >= 0 && lx < grid.GetLength(0) && ly < grid.GetLength(1))
                grid[lx, ly] = value;
            InvalidateMap();
        }

        bool ResolveTile(Point global, out int fileId, out int lx, out int ly)
        {
            fileId = -1; lx = 0; ly = 0;
            if (occupancy == null || host == null || host.MapFiles == null) return false;
            int cx = global.X / TilesPerCell;
            int cy = global.Y / TilesPerCell;
            if (cx < 0 || cy < 0 || cx >= occupancy.GetLength(0) || cy >= occupancy.GetLength(1)) return false;
            fileId = occupancy[cx, cy];
            if (fileId < 0) return false;
            lx = global.X - cx * TilesPerCell;
            ly = global.Y - cy * TilesPerCell;
            return true;
        }

        void EditTilePlanes(Point global)
        {
            int fileId, lx, ly;
            if (!ResolveTile(global, out fileId, out lx, out ly)) return;
            byte[] planes = MapPermissionReader.GetPlanes(host.MapFiles, fileId, lx, ly, PermBlock);
            if (planes == null || planes.Length == 0) return;

            var sb = new StringBuilder();
            for (int i = 0; i < planes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(planes[i].ToString("X2"));
            }

            using (var dlg = new Form())
            {
                dlg.Text = "Tile (" + global.X + ", " + global.Y + ") planes";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(420, 110);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;

                var hint = new Label
                {
                    AutoSize = false,
                    Bounds = new Rectangle(12, 10, 396, 32),
                    Text = "Eight hex bytes for this tile only. Space-separated. Cancel writes nothing."
                };
                var box = new TextBox
                {
                    Bounds = new Rectangle(12, 44, 396, 22),
                    Font = new Font("Consolas", 10f),
                    Text = sb.ToString()
                };
                var ok = new Button { Text = "Write", DialogResult = DialogResult.OK, Bounds = new Rectangle(232, 74, 80, 26) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(318, 74, 80, 26) };
                dlg.Controls.Add(hint);
                dlg.Controls.Add(box);
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

                string[] parts = box.Text.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != planes.Length)
                {
                    MessageBox.Show("Need exactly " + planes.Length + " bytes.");
                    return;
                }
                var next = new byte[planes.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    try { next[i] = Convert.ToByte(parts[i], 16); }
                    catch
                    {
                        MessageBox.Show("Bad hex: " + parts[i]);
                        return;
                    }
                }
                if (!MapPermissionReader.SetPlanes(host.MapFiles, fileId, lx, ly, next, PermBlock))
                {
                    MessageBox.Show("Could not write this tile.");
                    return;
                }
                byte[,] grid;
                if (perms.TryGetValue(fileId, out grid) && grid != null &&
                    lx >= 0 && ly >= 0 && lx < grid.GetLength(0) && ly < grid.GetLength(1))
                    grid[lx, ly] = next.Length > 4 ? next[4] : next[0];
                InvalidateMap();
            }
        }

        int TileType(int globalX, int globalY)
        {
            if (occupancy == null) return -1;
            int cx = globalX / TilesPerCell;
            int cy = globalY / TilesPerCell;
            if (cx < 0 || cy < 0 || cx >= occupancy.GetLength(0) || cy >= occupancy.GetLength(1)) return -1;
            int fileId = ResolveFile(occupancy[cx, cy]);
            byte[,] grid;
            if (!perms.TryGetValue(fileId, out grid) || grid == null) return -1;
            int lx = globalX - cx * TilesPerCell;
            int ly = globalY - cy * TilesPerCell;
            if (lx < 0 || ly < 0 || lx >= grid.GetLength(0) || ly >= grid.GetLength(1)) return -1;
            return grid[lx, ly];
        }

        void ResizeCanvas()
        {
            int w = Math.Max(32, mapW * tilePx + 1);
            int h = Math.Max(32, mapH * tilePx + 1);
            canvas.Size = new Size(w, h);
        }

        public void InvalidateMap()
        {
            if (canvas != null) canvas.Invalidate();
        }

        public void ScrollSelectedIntoView()
        {
            if (host == null || host.CurrentObjects == null || host.CurrentObjects.NPCs == null) return;
            int i = host.SelectedNpcIndex;
            if (i < 0 || i >= host.CurrentObjects.NPCs.Count) return;
            var n = host.CurrentObjects.NPCs[i];
            int sx = (n.xPosition - originX) * tilePx;
            int sy = (n.yPosition - originY) * tilePx;
            ((MapScrollPanel)canvasHost).SetScroll(
                Math.Max(0, sx - canvasHost.ClientSize.Width / 2),
                Math.Max(0, sy - canvasHost.ClientSize.Height / 2));
        }

        Point TileFromMouse(MouseEventArgs e)
        {
            int x = originX + e.X / Math.Max(1, tilePx);
            int y = originY + e.Y / Math.Max(1, tilePx);
            int maxX = originX + mapW - 1;
            int maxY = originY + mapH - 1;
            if (x < originX) x = originX;
            if (y < originY) y = originY;
            if (x > maxX) x = maxX;
            if (y > maxY) y = maxY;
            return new Point(x, y);
        }

        int HitNpc(Point tile)
        {
            var objs = host == null ? null : host.CurrentObjects;
            if (objs == null || objs.NPCs == null) return -1;

            int found = -1;
            int best = int.MaxValue;
            for (int i = 0; i < objs.NPCs.Count; i++)
            {
                var n = objs.NPCs[i];
                int dx = n.xPosition - tile.X;
                int dy = n.yPosition - tile.Y;
                int d = dx * dx + dy * dy;
                if (d > 1) continue; // same tile or orthogonally adjacent
                if (d < best || (d == best && i == host.SelectedNpcIndex))
                {
                    best = d;
                    found = i;
                }
            }
            return found;
        }

        int HitProxy(Point tile)
        {
            var objs = host == null ? null : host.CurrentObjects;
            if (objs == null || objs.furniture == null) return -1;
            int found = -1;
            int best = int.MaxValue;
            for (int i = 0; i < objs.furniture.Count; i++)
            {
                var f = objs.furniture[i];
                if (f.rail) continue;
                int dx = f.gridX - tile.X;
                int dy = f.gridZ - tile.Y;
                int d = dx * dx + dy * dy;
                if (d > 1) continue;
                if (d < best || (d == best && i == host.SelectedProxyIndex))
                {
                    best = d;
                    found = i;
                }
            }
            return found;
        }

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                panning = true;
                panLast = e.Location;
                canvas.Capture = true;
                canvas.Cursor = Cursors.SizeAll;
                return;
            }
            Point t = TileFromMouse(e);
            lastTile = t;
            dragStartPixel = e.Location;
            if ((ModifierKeys & Keys.Alt) == Keys.Alt && e.Button == MouseButtons.Right)
            {
                EditTilePlanes(t);
                return;
            }
            if (editPermsBox != null && editPermsBox.Checked &&
                (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) &&
                (ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                paintingPerms = true;
                paintValue = (e.Button == MouseButtons.Left)
                    ? (byte)MapPermissionReader.TypeWalk
                    : (byte)MapPermissionReader.TypeBlocked;
                PaintPermission(t, paintValue);
                canvas.Capture = true;
                return;
            }
            if (host == null || host.CurrentObjects == null) return;
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                host.PlaceBlankNpcAt((short)t.X, (short)t.Y);
                dragNpc = -1;
                dragProxy = -1;
                dragging = false;
                InvalidateMap();
                return;
            }
            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) == Keys.Control)
            {
                host.PlaceProxyAt(t.X, t.Y);
                dragNpc = -1;
                dragProxy = -1;
                dragging = false;
                InvalidateMap();
                return;
            }
            bool proxyFirst = host.ProxyTabActive;
            int proxyHit = (showProxiesBox != null && showProxiesBox.Checked) ? HitProxy(t) : -1;
            int hit = HitNpc(t);
            if (proxyFirst && proxyHit >= 0) hit = -1;
            if (!proxyFirst && hit >= 0) proxyHit = -1;
            if (hit >= 0)
            {
                // Don't recenter — scrolling under a held click was moving the NPC.
                host.SelectNpc(hit, false);
                dragProxy = -1;
                if (e.Button == MouseButtons.Left)
                {
                    dragNpc = hit;
                    dragging = false; // wait for a real drag
                    canvas.Capture = true;
                }
            }
            else if (proxyHit >= 0)
            {
                host.SelectProxy(proxyHit);
                dragNpc = -1;
                if (e.Button == MouseButtons.Left)
                {
                    dragProxy = proxyHit;
                    dragging = false;
                    canvas.Capture = true;
                }
            }
            else
            {
                dragNpc = -1;
                dragProxy = -1;
                dragging = false;
            }
            InvalidateMap();
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                int dx = e.X - panLast.X;
                int dy = e.Y - panLast.Y;
                Point pos = canvasHost.AutoScrollPosition;
                ((MapScrollPanel)canvasHost).SetScroll(-pos.X - dx, -pos.Y - dy);
                return;
            }

            Point t = TileFromMouse(e);
            if (t != hoverTile)
            {
                hoverTile = t;
                UpdateHoverTip(t);
            }

            if (paintingPerms)
            {
                if (t != lastTile) PaintPermission(t, paintValue);
                lastTile = t;
                return;
            }

            if ((dragNpc < 0 && dragProxy < 0) || host == null || host.CurrentObjects == null) return;
            if (!dragging)
            {
                int dx = e.X - dragStartPixel.X;
                int dy = e.Y - dragStartPixel.Y;
                if (dx * dx + dy * dy < 16 && t == lastTile) return;
                dragging = true;
            }
            if (t == lastTile) return;
            lastTile = t;
            if (dragNpc >= 0)
                host.MoveNpcTo(dragNpc, (short)t.X, (short)t.Y, persist: false);
            else if (dragProxy >= 0)
                host.MoveProxyTo(dragProxy, t.X, t.Y, persist: false);
            InvalidateMap();
        }

        void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                panning = false;
                canvas.Capture = false;
                canvas.Cursor = Cursors.Default;
                return;
            }
            if (paintingPerms)
            {
                paintingPerms = false;
                canvas.Capture = false;
                if (editPermsBox != null && editPermsBox.Checked)
                    canvas.Cursor = Cursors.Cross;
                InvalidateMap();
                return;
            }
            if (dragging && dragNpc >= 0 && host != null)
                host.MoveNpcTo(dragNpc, (short)lastTile.X, (short)lastTile.Y, persist: true);
            if (dragging && dragProxy >= 0 && host != null)
                host.MoveProxyTo(dragProxy, lastTile.X, lastTile.Y, persist: true);
            dragging = false;
            dragNpc = -1;
            dragProxy = -1;
            canvas.Capture = false;
            InvalidateMap();
        }

        void ZoomBarChanged(object sender, EventArgs e)
        {
            if (suppressZoomEvent) return;
            SetZoom(zoomBar.Value, null);
        }

        void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            Point view = canvasHost.PointToClient(Control.MousePosition);
            int next = tilePx + (e.Delta > 0 ? 2 : -2);
            if (next < zoomBar.Minimum) next = zoomBar.Minimum;
            if (next > zoomBar.Maximum) next = zoomBar.Maximum;
            SetZoom(next, view);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        void SetZoom(int next, Point? viewAnchor)
        {
            if (next == tilePx)
            {
                if (zoomBar.Value != next)
                {
                    suppressZoomEvent = true;
                    zoomBar.Value = next;
                    suppressZoomEvent = false;
                }
                return;
            }

            Point view = viewAnchor ?? new Point(canvasHost.ClientSize.Width / 2, canvasHost.ClientSize.Height / 2);
            Point scroll = canvasHost.AutoScrollPosition;
            float worldX = (view.X - scroll.X) / (float)Math.Max(1, tilePx);
            float worldY = (view.Y - scroll.Y) / (float)Math.Max(1, tilePx);

            tilePx = next;
            if (zoomBar.Value != next)
            {
                suppressZoomEvent = true;
                zoomBar.Value = next;
                suppressZoomEvent = false;
            }
            ResizeCanvas();

            int newX = (int)Math.Round(worldX * tilePx) - view.X;
            int newY = (int)Math.Round(worldY * tilePx) - view.Y;
            ((MapScrollPanel)canvasHost).SetScroll(Math.Max(0, newX), Math.Max(0, newY));
            InvalidateMap();
        }

        void UpdateHoverTip(Point t)
        {
            var objs = host == null ? null : host.CurrentObjects;
            string tip = string.Format("tile ({0}, {1})", t.X, t.Y);
            if (occupancy != null)
            {
                int cx = t.X / TilesPerCell;
                int cy = t.Y / TilesPerCell;
                if (cx >= 0 && cy >= 0 && cx < occupancy.GetLength(0) && cy < occupancy.GetLength(1))
                {
                    tip += string.Format("  cell {0},{1}  mapFile {2}", cx, cy, occupancy[cx, cy]);
                    if (headers != null && cx < headers.GetLength(0) && cy < headers.GetLength(1))
                        tip += "  hdr " + headers[cx, cy];
                }
            }
            int tt = TileType(t.X, t.Y);
            if (tt >= 0) tip += "  " + MapPermissionReader.TypeName(tt) + " (" + tt + ")";
            if (occupancy != null && host != null && host.MapFiles != null)
            {
                int cx = t.X / TilesPerCell;
                int cy = t.Y / TilesPerCell;
                if (cx >= 0 && cy >= 0 && cx < occupancy.GetLength(0) && cy < occupancy.GetLength(1))
                {
                    int fileId = ResolveFile(occupancy[cx, cy]);
                    int lx = t.X - cx * TilesPerCell;
                    int ly = t.Y - cy * TilesPerCell;
                    string dump = MapPermissionReader.DumpTile(host.MapFiles, fileId, lx, ly, PermBlock);
                    if (!string.IsNullOrEmpty(dump)) tip += "\n" + dump;
                }
            }
            if (objs != null && objs.NPCs != null)
            {
                for (int i = 0; i < objs.NPCs.Count; i++)
                {
                    var n = objs.NPCs[i];
                    if (n.xPosition != t.X || n.yPosition != t.Y) continue;
                    tip += string.Format("\nNPC #{0}  sprite {1}  script {2}  flag {3}  dir {4}",
                        i, n.sprite, n.scriptUsed, n.flag, n.defaultDirection);
                }
            }
            if (objs != null && objs.furniture != null)
            {
                for (int i = 0; i < objs.furniture.Count; i++)
                {
                    var f = objs.furniture[i];
                    if (f.rail || f.gridX != t.X || f.gridZ != t.Y) continue;
                    string face = f.interactibility >= 0 && f.interactibility < OverworldFurniture.InteractNames.Length
                        ? OverworldFurniture.InteractNames[f.interactibility]
                        : f.interactibility.ToString();
                    tip += string.Format("\nProxy #{0}  script {1}  cond {2}  {3}", i, f.scriptUsed, f.condition, face);
                }
            }
            hoverTip.SetToolTip(canvas, tip);
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Color.FromArgb(18, 20, 24));

            int cellsX = occupancy != null ? occupancy.GetLength(0) : 1;
            int cellsY = occupancy != null ? occupancy.GetLength(1) : 1;
            int zoneIndex = host != null && host.CurrentZone != null ? host.CurrentZone.index : -1;

            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int fileId = occupancy != null ? occupancy[cx, cy] : 0;
                    int header = headers != null && cx < headers.GetLength(0) && cy < headers.GetLength(1) ? headers[cx, cy] : -1;
                    int tileX = cx * TilesPerCell - originX;
                    int tileY = cy * TilesPerCell - originY;
                    if (tileX + TilesPerCell <= 0 || tileY + TilesPerCell <= 0 || tileX >= mapW || tileY >= mapH)
                        continue;

                    bool empty = fileId < 0;
                    bool inZone = header < 0 || header == zoneIndex;
                    bool allTiles = allTilesBox.Checked;
                    Color fill = empty
                        ? Color.FromArgb(28, 30, 36)
                        : ColorFromCell(cx + cy * 7 + fileId);
                    if (!inZone && !allTiles) fill = Color.FromArgb(fill.R / 3, fill.G / 3, fill.B / 3);

                    Rectangle cellRect = new Rectangle(tileX * tilePx, tileY * tilePx, TilesPerCell * tilePx, TilesPerCell * tilePx);
                    using (var b = new SolidBrush(fill))
                        g.FillRectangle(b, cellRect);
                    if (empty)
                    {
                        using (var hatch = new HatchBrush(HatchStyle.Percent20, Color.FromArgb(allTiles ? 70 : 40, 54, 62), fill))
                            g.FillRectangle(hatch, cellRect);
                    }

                    byte[,] grid;
                    if (showTerrainBox.Checked && fileId >= 0 && perms.TryGetValue(fileId, out grid) && grid != null)
                    {
                        int gw = grid.GetLength(0);
                        int gh = grid.GetLength(1);
                        for (int ly = 0; ly < gh && ly < TilesPerCell; ly++)
                        {
                            for (int lx = 0; lx < gw && lx < TilesPerCell; lx++)
                            {
                                int tt = grid[lx, ly];
                                int px = (tileX + lx) * tilePx;
                                int py = (tileY + ly) * tilePx;
                                bool blocked = MapPermissionReader.IsBlocked(tt) && !MapPermissionReader.IsLedge(tt);
                                if (!allTiles && blocked)
                                {
                                    using (var tb = new SolidBrush(Color.FromArgb(inZone ? 38 : 22, 32, 30)))
                                        g.FillRectangle(tb, px, py, tilePx, tilePx);
                                    continue;
                                }
                                Color tc = TerrainColor(tt, inZone || allTiles);
                                using (var tb = new SolidBrush(tc))
                                    g.FillRectangle(tb, px, py, tilePx, tilePx);
                                if (blocked)
                                {
                                    using (var hatch = new HatchBrush(HatchStyle.Percent25, Color.FromArgb(70, 62, 56), tc))
                                        g.FillRectangle(hatch, px, py, tilePx, tilePx);
                                }
                                if (MapPermissionReader.IsLedge(tt))
                                    DrawLedgeMark(g, px, py, tilePx, tt);
                            }
                        }
                    }

                    using (var p = new Pen(inZone ? Color.FromArgb(90, 110, 130) : Color.FromArgb(40, 46, 54), 1))
                        g.DrawRectangle(p, tileX * tilePx, tileY * tilePx, TilesPerCell * tilePx, TilesPerCell * tilePx);

                    if (tilePx >= 10 && fileId >= 0)
                    {
                        using (var font = new Font("Segoe UI", 7f))
                        using (var tb = new SolidBrush(Color.FromArgb(160, 200, 210, 220)))
                            g.DrawString(fileId.ToString(), font, tb, tileX * tilePx + 3, tileY * tilePx + 2);
                    }
                }
            }

            using (var p = new Pen(Color.FromArgb(28, 40, 50)))
            {
                for (int x = 0; x <= mapW; x++)
                    g.DrawLine(p, x * tilePx, 0, x * tilePx, mapH * tilePx);
                for (int y = 0; y <= mapH; y++)
                    g.DrawLine(p, 0, y * tilePx, mapW * tilePx, y * tilePx);
            }

            var objs = host == null ? null : host.CurrentObjects;
            if (objs == null) return;

            if (objs.NPCs != null && (showLeashBox.Checked || showSightBox.Checked))
                DrawLeashAndSight(g, objs.NPCs);

            if (showWarpsBox.Checked && objs.warps != null)
            {
                using (var b = new SolidBrush(Color.FromArgb(180, 80, 200)))
                {
                    foreach (var w in objs.warps)
                    {
                        float wx = WarpTile(w.exitX, w.rail) - originX;
                        // Map Y is the editor's Exit Z. Exit Y is height.
                        float wy = WarpTile(w.exitZ, w.rail) - originY;
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
                        g.FillRectangle(b, (t.xPosition - originX) * tilePx, (t.yPosition - originY) * tilePx, tw * tilePx, th * tilePx);
                        g.DrawRectangle(pen, (t.xPosition - originX) * tilePx, (t.yPosition - originY) * tilePx, tw * tilePx, th * tilePx);
                    }
                }
            }

            if (showProxiesBox != null && showProxiesBox.Checked && objs.furniture != null)
                DrawProxies(g, objs.furniture);

            if (objs.NPCs == null) return;
            int selected = host.SelectedNpcIndex;

            // Stack offsets so two NPCs on the same tile stay clickable/visible.
            int[] stack = new int[objs.NPCs.Count];
            for (int i = 0; i < objs.NPCs.Count; i++)
            {
                int k = 0;
                for (int j = 0; j < i; j++)
                {
                    if (objs.NPCs[j].xPosition == objs.NPCs[i].xPosition &&
                        objs.NPCs[j].yPosition == objs.NPCs[i].yPosition)
                        k++;
                }
                stack[i] = k;
            }

            using (var font = new Font("Segoe UI", Math.Max(6f, tilePx * 0.5f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < objs.NPCs.Count; i++)
                {
                    var n = objs.NPCs[i];
                    int px = (n.xPosition - originX) * tilePx + stack[i] * Math.Max(3, tilePx / 4);
                    int py = (n.yPosition - originY) * tilePx - stack[i] * Math.Max(3, tilePx / 4);
                    bool sel = i == selected;
                    DrawNpcMarker(g, px, py, tilePx, i, n, sel, font, sf);
                }
            }
        }

        void DrawProxies(Graphics g, System.Collections.Generic.IList<OverworldFurniture> proxies)
        {
            int selected = host == null ? -1 : host.SelectedProxyIndex;
            using (var font = new Font("Segoe UI", Math.Max(6f, tilePx * 0.45f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var fill = new SolidBrush(Color.FromArgb(150, 40, 90, 220)))
            using (var selFill = new SolidBrush(Color.FromArgb(210, 220, 50, 50)))
            using (var pen = new Pen(Color.FromArgb(230, 210, 225, 255)))
            using (var selPen = new Pen(Color.White, 2))
            {
                for (int i = 0; i < proxies.Count; i++)
                {
                    var f = proxies[i];
                    if (f.rail) continue;
                    int px = (f.gridX - originX) * tilePx;
                    int py = (f.gridZ - originY) * tilePx;
                    bool sel = i == selected;
                    var rect = new Rectangle(px + 2, py + 2, Math.Max(4, tilePx - 4), Math.Max(4, tilePx - 4));
                    g.FillRectangle(sel ? selFill : fill, rect);
                    g.DrawRectangle(sel ? selPen : pen, rect);
                    g.DrawString(i.ToString(), font, Brushes.White, rect, sf);
                    DrawProxyFacing(g, rect, f.interactibility, sel ? Color.White : Color.FromArgb(180, 210, 255));
                }
            }
        }

        static void DrawProxyFacing(Graphics g, Rectangle rect, short face, Color c)
        {
            // 0 south, 1 west, 2 east, 3 north, 4 all, 5 west/east, 6 north/south
            using (var pen = new Pen(c, 1))
            {
                int cx = rect.X + rect.Width / 2;
                int cy = rect.Y + rect.Height / 2;
                if (face == 4)
                {
                    g.DrawEllipse(pen, rect.X + 2, rect.Y + 2, Math.Max(2, rect.Width - 4), Math.Max(2, rect.Height - 4));
                    return;
                }
                if (face == 0 || face == 6) g.DrawLine(pen, cx, cy, cx, rect.Bottom - 1);
                if (face == 3 || face == 6) g.DrawLine(pen, cx, cy, cx, rect.Y + 1);
                if (face == 1 || face == 5) g.DrawLine(pen, cx, cy, rect.X + 1, cy);
                if (face == 2 || face == 5) g.DrawLine(pen, cx, cy, rect.Right - 1, cy);
            }
        }

        static void DrawNpcMarker(Graphics g, int px, int py, int tilePx, int index, OverworldNPC n, bool sel, Font font, StringFormat sf)
        {
            int s = Math.Max(8, tilePx);
            Rectangle body = new Rectangle(px + 1, py + 1, s - 2, s - 2);
            Color fill = sel ? Color.FromArgb(255, 230, 80) : SpriteColor(n.sprite);
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(sel ? Color.White : Color.FromArgb(20, 20, 20), sel ? 2 : 1))
            {
                g.FillEllipse(b, body);
                g.DrawEllipse(pen, body);
            }

            using (var tb = new SolidBrush(sel ? Color.Black : Color.White))
                g.DrawString(index.ToString(), font, tb, new RectangleF(px, py - 1, s, s), sf);

            DrawFacing(g, px, py, s, n.defaultDirection, sel ? Color.Black : Color.White);
        }

        void DrawLeashAndSight(Graphics g, System.Collections.Generic.IList<OverworldNPC> npcs)
        {
            using (var leashFill = new SolidBrush(Color.FromArgb(55, 70, 140, 220)))
            using (var leashPen = new Pen(Color.FromArgb(160, 80, 160, 230)))
            using (var sightFill = new SolidBrush(Color.FromArgb(70, 210, 50, 40)))
            using (var sightPen = new Pen(Color.FromArgb(200, 220, 60, 50)))
            {
                foreach (var n in npcs)
                {
                    if (showLeashBox.Checked)
                    {
                        int hx = Math.Max(0, (int)n.horizontalLeash);
                        int vy = Math.Max(0, (int)n.verticalLeash);
                        if (hx > 0 || vy > 0)
                        {
                            int x = n.xPosition - hx - originX;
                            int y = n.yPosition - vy - originY;
                            int w = hx * 2 + 1;
                            int h = vy * 2 + 1;
                            g.FillRectangle(leashFill, x * tilePx, y * tilePx, w * tilePx, h * tilePx);
                            g.DrawRectangle(leashPen, x * tilePx, y * tilePx, w * tilePx, h * tilePx);
                        }
                    }
                    if (showSightBox.Checked && n.sightRange > 0)
                    {
                        int dx, dy;
                        FacingDelta(n.defaultDirection, out dx, out dy);
                        if (dx != 0 || dy != 0)
                        {
                            for (int s = 1; s <= n.sightRange; s++)
                            {
                                int sx = n.xPosition + dx * s - originX;
                                int sy = n.yPosition + dy * s - originY;
                                g.FillRectangle(sightFill, sx * tilePx, sy * tilePx, tilePx, tilePx);
                                g.DrawRectangle(sightPen, sx * tilePx, sy * tilePx, tilePx, tilePx);
                            }
                        }
                    }
                }
            }
        }

        static void FacingDelta(short dir, out int dx, out int dy)
        {
            // Confirmed in-editor: 0 north, 1 south, 2 west, 3 east.
            switch (dir)
            {
                case 0: dx = 0; dy = -1; return;
                case 1: dx = 0; dy = 1; return;
                case 2: dx = -1; dy = 0; return;
                case 3: dx = 1; dy = 0; return;
                default: dx = 0; dy = 0; return;
            }
        }

        static void DrawFacing(Graphics g, int px, int py, int s, short dir, Color c)
        {
            int dx, dy;
            FacingDelta(dir, out dx, out dy);
            if (dx == 0 && dy == 0) return;
            int cx = px + s / 2;
            int cy = py + s / 2;
            int tx = cx + dx * Math.Max(3, s / 2 - 1);
            int ty = cy + dy * Math.Max(3, s / 2 - 1);
            using (var pen = new Pen(c, 1))
                g.DrawLine(pen, cx, cy, tx, ty);
        }

        static float WarpTile(short raw, bool rail)
        {
            if (rail) return raw;
            return (raw - 8) / 16f;
        }

        static float WarpTileY(short raw, bool rail)
        {
            if (rail) return raw;
            return raw / 16f;
        }

        static void DrawLedgeMark(Graphics g, int px, int py, int s, int type)
        {
            using (var b = new SolidBrush(Color.FromArgb(210, 150, 70)))
            {
                int m = Math.Max(1, s / 5);
                // 115 is the common BW2 one-way ledge; 116/117 match SDSME N/S.
                if (type == MapPermissionReader.TypeLedgeW || type == MapPermissionReader.TypeLedgeE)
                {
                    int x = type == MapPermissionReader.TypeLedgeE ? px + s - m - 1 : px + m;
                    g.FillRectangle(b, x, py + 1, Math.Max(2, s / 4), s - 2);
                }
                else
                {
                    int y = (type == MapPermissionReader.TypeLedgeN) ? py + m : py + s - m - Math.Max(2, s / 4);
                    g.FillRectangle(b, px + 1, y, s - 2, Math.Max(2, s / 4));
                }
            }
        }

        static Color TerrainColor(int type, bool inZone)
        {
            Color c;
            if (MapPermissionReader.IsLedge(type))
                c = Color.FromArgb(128, 96, 48);
            else if (MapPermissionReader.IsBlocked(type))
                c = Color.FromArgb(96, 86, 78);
            else if (type == MapPermissionReader.TypeDarkGrass)
                c = Color.FromArgb(28, 78, 34);
            else if (type == MapPermissionReader.TypeTallGrass)
                c = Color.FromArgb(46, 112, 44);
            else if (type == MapPermissionReader.TypeShortGrass || type == 31)
                c = Color.FromArgb(70, 130, 52);
            else if (MapPermissionReader.IsGrass(type))
                c = Color.FromArgb(58, 118, 48);
            else if (type == MapPermissionReader.TypeSwamp)
                c = Color.FromArgb(40, 90, 78);
            else if (type == MapPermissionReader.TypeSurfEdge)
                c = Color.FromArgb(18, 48, 112);
            else if (MapPermissionReader.IsWater(type))
                c = Color.FromArgb(42, 92, 168);
            else if (type == MapPermissionReader.TypeCave)
                c = Color.FromArgb(78, 68, 58);
            else if (type == MapPermissionReader.TypeSnow)
                c = Color.FromArgb(186, 196, 206);
            else if (type == 0)
                c = Color.FromArgb(86, 92, 72);
            else
                c = Color.FromArgb(70, 74, 80);
            if (!inZone)
                c = Color.FromArgb(c.R / 3, c.G / 3, c.B / 3);
            return c;
        }

        static Color ColorFromCell(int seed)
        {
            int h = Math.Abs(seed * 47) % 360;
            return FromHsv(h, 0.25f, 0.32f);
        }

        static Color SpriteColor(int sprite)
        {
            int h = Math.Abs(sprite * 37 + 80) % 360;
            return FromHsv(h, 0.55f, 0.75f);
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

        /// <summary>
        /// AutoScroll Panel that does not jump when a child control receives focus.
        /// Default Panel.ScrollToControl would snap the PictureBox origin under
        /// the mouse whenever a scrollbar is showing.
        /// </summary>
        sealed class NoFocusBox : PictureBox
        {
            public NoFocusBox()
            {
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
            }
        }

        sealed class MapScrollPanel : Panel
        {
            public MapScrollPanel()
            {
                AutoScroll = true;
                DoubleBuffered = true;
                TabStop = false;
            }

            protected override Point ScrollToControl(Control activeControl)
            {
                return AutoScrollPosition;
            }

            public void SetScroll(int x, int y)
            {
                AutoScrollPosition = new Point(Math.Max(0, x), Math.Max(0, y));
            }
        }
    }
}
