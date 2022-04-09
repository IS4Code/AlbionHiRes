/*
Copyright 2022 IS4

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using AlbLib;
using AlbLib.Imaging;
using AlbLib.Mapping;
using AlbLib.Texts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

Dictionary<int, (int, int)[]> palRotations = new Dictionary<int, (int, int)[]>
{
    {1,  new[]{ (0x99, 0x9f), (0xb0, 0xb4), (0xb5, 0xbf)}},
    {2,  new[]{ (0x99, 0x9f), (0xb0, 0xb4), (0xb5, 0xbf)}},
    {3,  new[]{ (0x40, 0x43), (0x44, 0x4f)}},
    {4,  new[]{ (0xb0, 0xb4), (0xb5, 0xbf)}}, //added
    {6,  new[]{ (0xb0, 0xb4), (0xb5, 0xbf)}},
    {14, new[]{ (0xb0, 0xb3), (0xb4, 0xbf)}},
    {15, new[]{ (0x58, 0x5f)}},
    {25, new[]{ (0xb0, 0xb3), (0xb4, 0xbf)}},
    {26, new[]{ (0xb4, 0xb7), (0xb8, 0xbb), (0xbc, 0xbf)}},
    {31, new[]{ (0x10, 0x4f)}},
    {47, new[]{ (0x99, 0x9f), (0xb0, 0xb4), (0xb5, 0xbf)}},
    {48, new[]{ (0xb0, 0xb4), (0xb5, 0xbf)}}, //added
    {49, new[]{ (0xb0, 0xb3), (0xb4, 0xbf)}},
    {51, new[]{ (0xb0, 0xb3), (0xb4, 0xbf)}},
    {55, new[]{ (0x40, 0x43), (0x44, 0x4f)}}
};

Dictionary<byte, byte> palNights = new Dictionary<byte, byte>
{
    {  1, 47 },
    {  2, 47 },
    {  3, 55 },
    {  4, 48 },
    { 14, 49 },
    { 25, 49 },
    { 51, 49 },
};

void ExportTileContexts(string baseDir)
{
    // Precache all maps in game
    var allMaps = GameData.Maps.ToList();

    // The size of margin of context around a tile
    const int planeMargin = 8;
    const int planeOffset = planeMargin - 16;
    const int planeSize = 16 + 2 * planeMargin;

    string lowresDir = Path.Combine(baseDir, "LR");
    Directory.CreateDirectory(lowresDir);
    string maskDir = lowresDir;

    // This file will store commands that combine images with masks
    var log = File.CreateText(Path.Combine(baseDir, "masknames.bat"));
    log.AutoFlush = true;

    var badMaps = new HashSet<int> { 165, 210, 211, 214, 293, 297, 388, 389, 390, 398, 399 };

    foreach(var pair in GameData.MapIcons.IndexEnumerate())
    {
        var icons = pair.Value;
        if(pair.Value == null) continue;
        var tileset = pair.Key;
        Console.WriteLine("Tileset: " + tileset);

        var processed = new HashSet<short>();

        foreach(var icon in icons)
        {
            // Do not process same graphics twice
            if(!processed.Add(icon.GrID))
            {
                continue;
            }

            Console.WriteLine("Icon: " + icon.Id);

            // Contains the all 3x3 contexts of a tile in all maps, encoded as strings
            var surrounding = new HashSet<string>();

            // The same but only for blocks
            var surroundingBlocks = new HashSet<string>();

            // Which palettes to draw this icon with
            var palettes = new HashSet<byte>();

            // How many times specific tiles appear around the center tile
            var tileCounts = new Dictionary<short, int>();

            // How many times an underlay appears under an overlay
            var globalUnderlayTileCounts = new Dictionary<short, int>();

            // Only go through these maps
            var validMaps = allMaps.Where(map => {
                if(map == null || map.Type != MapType.Map2D || map.Tileset != tileset) return false;
                if(map.Error != null || badMaps.Contains(map.Id)) return false;
                return true;
            });

            foreach(var map in validMaps)
            {
                palettes.Add(map.Palette);
            }

            // Also go through blocks
            IEnumerable<ITiled> sources = validMaps.Cast<ITiled>().Concat(GameData.Blocks[tileset].Blocks);

            foreach(var map in sources)
            {
                bool isMap = map is Map;

                foreach(var tile in map.TileData)
                {
                    bool hasOverlay = tile.Overlay > 1 && !icons[tile.Overlay - 2].Discard;
                    bool matchUnderlay = tile.Underlay == icon.Id && !hasOverlay;
                    bool matchOverlay = tile.Overlay == icon.Id;
                    if(matchUnderlay || matchOverlay)
                    {
                        // This is a 1x1 to 2x2 block; the context is not large enough to use
                        if(matchUnderlay && map.Width <= 2 && map.Height <= 2)
                        {
                            continue;
                        }

                        // The tile ids in the context
                        var list = new List<short>();

                        for(int y = -1; y <= 1; y++)
                        {
                            for(int x = -1; x <= 1; x++)
                            {
                                int ax = tile.X + x;
                                int ay = tile.Y + y;

                                // Add the underlay and overlay to the list (or 0 if at edge)
                                if(ax < 0 || ax >= map.Width || ay < 0 || ay >= map.Height)
                                {
                                    list.Add(0);
                                    list.Add(0);
                                }else{
                                    var tile2 = map.TileData[ax, ay];
                                    list.Add(tile2.Underlay);
                                    list.Add(tile2.Overlay);

                                    if(isMap)
                                    {
                                        // Also add the tile to the context tile counts
                                        void Add(short id, bool allowEmpty)
                                        {
                                            if(id <= 1 && !allowEmpty) return;

                                            if(!tileCounts.TryGetValue(id, out var count))
                                            {
                                                count = 0;
                                            }
                                            tileCounts[id] = count + 1;
                                        }
                                        Add(tile2.Underlay, false);
                                        Add(tile2.Overlay, true);
                                    }
                                }
                            }
                        }

                        var code = String.Join("|", list);
                        (isMap ? surrounding : surroundingBlocks).Add(code);
                    }

                    if(hasOverlay && tile.Underlay > 1)
                    {
                        // If this is a suitable underlay for tiles, add
                        if(!globalUnderlayTileCounts.TryGetValue(tile.Underlay, out var count))
                        {
                            count = 0;
                        }
                        globalUnderlayTileCounts[tile.Underlay] = count + 1;
                    }
                }
            }

            // Also add all night palettes
            // TODO: Dji Cantos isle doesn't have specific night palette,
            // but it is possible to create it (by halving color components)
            var nightPalettes = new HashSet<byte>();
            foreach(var pal in palettes)
            {
                if(palNights.TryGetValue(pal, out var night))
                {
                    nightPalettes.Add(night);
                }
            }
            foreach(var pal in nightPalettes)
            {
                palettes.Add(pal);
            }

            IEnumerable<List<int>> CreateContexts(IEnumerable<string> data)
            {
                // Decodes the context back into list of tile ids
                var lists = data.Select(s => s.Split('|').Select(c => Int32.Parse(c)).ToList()).ToList();
                // Order by a score ranging from the context that shares the most tiles with others
                return lists.OrderByDescending(p => p.Sum(c => tileCounts.TryGetValue((short)c, out var val) ? val : 0));
            }

            // Try normal maps, then blocks to pick the best context (might be null for unused tiles)
            var context = CreateContexts(surrounding).Concat(CreateContexts(surroundingBlocks)).FirstOrDefault();

            // Was this context created from an underlay?
            bool isUnderlay = context == null ? icon.IsUnderlay : context[8] == icon.Id;

            // Get the images for all the animations
            var baseFrames = Enumerable.Range(icon.GrID, icon.FramesCount).Select(frame => MapIcons.GetIconGraphics(tileset, frame)).ToList();

            // If any frame contains a transparent color
            bool drawMask = baseFrames.Any(frame => frame.ImageData.Any(c => c == 0));

            // Draw the color image and mask in a similar fashion
            foreach(bool drawingMask in drawMask ? new[] { false, true } : new[] { false })
            {
                // Use the underlay that is used the most as background
                RawImage suitableUnderlay = null;
                if(!drawingMask)
                {
                    var suitableUnderlayId = globalUnderlayTileCounts.OrderByDescending(p => p.Value).FirstOrDefault().Key;
                    if(suitableUnderlayId > 1)
                    {
                        suitableUnderlay = MapIcons.GetIconGraphics(tileset, icons[suitableUnderlayId - 2].GrID);
                    }
                }

                var frames = Enumerable.Range(0, icon.FramesCount).Select(frame => {
                    var plane = new GraphicPlane(planeSize, planeSize);
                    var objects = plane.Objects;

                    if(context != null)
                    {
                        // We have a context
                        for(int y = 0; y <= 2; y++)
                        {
                            for(int x = 0; x <= 2; x++)
                            {
                                var index = (y * 3 + x) * 2;
                                var underlay = context[index];
                                var overlay = context[index + 1];

                                var pos = new Point(planeOffset + x * 16, planeOffset + y * 16);

                                if(suitableUnderlay != null && underlay <= 1)
                                {
                                    // If we don't have an underlay, use the background
                                    objects.Add(new GraphicObject(suitableUnderlay, pos));
                                }

                                void Add(int id)
                                {
                                    if(id == icon.Id)
                                    {
                                        objects.Add(new GraphicObject(baseFrames[frame], pos));
                                    }else if(id >= 2)
                                    {
                                        var iconData = icons[id - 2];
                                        if(!iconData.Discard)
                                        {
                                            var gr = iconData.GrID + frame % iconData.FramesCount;
                                            objects.Add(new GraphicObject(MapIcons.GetIconGraphics(tileset, gr), pos));
                                        }
                                    }
                                }
                                // Do not draw underlay if this is a mask and we are drawing an overlay
                                if(!drawingMask || isUnderlay) Add(underlay);
                                Add(overlay);
                            }
                        }
                    }else{
                        // We do not have a context - use mirrored base
                        var image = baseFrames[frame];
                        RawImage FlipH(byte[] from)
                        {
                            var to = new byte[image.ImageData.Length];
                            for(int y = 0; y < image.Height; y++)
                            {
                                for(int x = 0; x < image.Width; x++)
                                {
                                    to[y * image.Width + x] = from[(y + 1) * image.Width - 1 - x];
                                }
                            }
                            return new RawImage(to, image.Width, image.Height);
                        }
                        RawImage FlipV(byte[] from)
                        {
                            var to = new byte[image.ImageData.Length];
                            for(int y = 0; y < image.Height; y++)
                            {
                                Array.Copy(from, (image.Height - 1 - y) * image.Width, to, y * image.Width, image.Height);
                            }
                            return new RawImage(to, image.Width, image.Height);
                        }
                        var flippedH = FlipH(image.ImageData);
                        var flippedV = FlipV(image.ImageData);
                        var flippedHV = FlipH(flippedV.ImageData);

                        var images = new[]
                        {
                            new[] { flippedHV, flippedV, flippedHV },
                            new[] { flippedH, image, flippedH },
                            new[] { flippedHV, flippedV, flippedHV },
                        };

                        for(int y = 0; y <= 2; y++)
                        {
                            for(int x = 0; x <= 2; x++)
                            {
                                var pos = new Point(planeOffset + x * 16, planeOffset + y * 16);
                                if(suitableUnderlay != null && !isUnderlay)
                                {
                                    // If we don't have an underlay, use the background
                                    objects.Add(new GraphicObject(suitableUnderlay, pos));
                                }
                                objects.Add(new GraphicObject(images[y][x], pos));
                            }
                        }
                    }

                    plane.Bake();
                    return plane.Background;
                }).ToList();

                if(!drawingMask)
                {
                    // We are not drawing a mask
                    foreach(var palette in palettes)
                    {
                        if(palRotations.TryGetValue(palette, out var rots))
                        {
                            // Are there any color rotations used by the tile?
                            var used = new bool[rots.Length];
                            foreach(var color in baseFrames.SelectMany(frame => frame.ImageData))
                            {
                                for(int i = 0; i < rots.Length; i++)
                                {
                                    var rot = rots[i];
                                    if(rot.Item1 <= color && color <= rot.Item2)
                                    {
                                        used[i] = true;
                                        break;
                                    }
                                }
                                if(!used.Contains(false))
                                {
                                    break;
                                }
                            }
                            rots = rots.Where((rot, i) => used[i]).ToArray();
                        }else{
                            rots = new (int, int)[0];
                        }

                        var paletteData = ImagePalette.GetFullPalette(palette);
                        Color[] paletteArray = null;

                        if(rots.Length > 0)
                        {
                            // Duplicate the palette if we have to do rotations and use our custom array
                            paletteArray = new Color[paletteData.Length];
                            paletteData.CopyTo(paletteArray, 0);
                            paletteData = ImagePalette.Create(paletteArray);
                        }

                        // How many frames of palette rotation in total? lcm(#rot1, #rot2, #rot3...)
                        long palFrames = rots.Aggregate(1L, (s, p) => lcm(s, p.Item2 - p.Item1 + 1));

                        for(long palFrame = 0; palFrame < palFrames; palFrame++)
                        {
                            for(int imgFrame = 0; imgFrame < frames.Count; imgFrame++)
                            {
                                // Render each combination of palette and frame
                                var img = frames[imgFrame].Render(paletteData);

                                string dirName = Path.Combine(lowresDir, $"{tileset:00}-{palette:00}");
                                Directory.CreateDirectory(dirName);

                                var name = $"{icon.Id:0000}_{icon.GrID + imgFrame:0000}_{palFrame:000}.png";

                                img.Save(Path.Combine(dirName, name));

                                // Link the file to mask
                                if(drawMask)
                                {
                                    log.WriteLine($"call drawMasked {tileset:00}-{palette:00}/{name} {tileset:00}/{icon.Id:0000}_{icon.GrID + imgFrame:0000}.png");
                                }else{
                                    log.WriteLine($"call drawUnmasked {tileset:00}-{palette:00}/{name}");
                                }
                            }

                            // Perform rotations on the palette
                            foreach(var (from, to) in rots)
                            {
                                var last = paletteArray[to];
                                for(int i = to; i > from; i--)
                                {
                                    paletteArray[i] = paletteArray[i - 1];
                                }
                                paletteArray[from] = last;
                            }
                        }
                    }
                }else{
                    // We are drawing a mask
                    for(int frame = 0; frame < frames.Count; frame++)
                    {
                        string dirName = Path.Combine(maskDir, $"{tileset:00}");
                        Directory.CreateDirectory(dirName);

                        var name = $"{icon.Id:0000}_{icon.GrID + frame:0000}.png";

                        var img = frames[frame].Render(ImagePalette.Monochrome);
                        img.Save(Path.Combine(dirName, name));
                    }
                }
            }
        }
    }

    log.Close();
}
