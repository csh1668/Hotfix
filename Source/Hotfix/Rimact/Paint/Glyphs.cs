using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rimact;

/// <summary>Vector icons, usable wherever an Icon goes. Without a Tint they take the text color, hover included.</summary>
internal enum Glyph : byte
{
    Check, X, ChevronUp, ChevronDown, ChevronLeft, ChevronRight, ArrowUp, ArrowDown, ArrowLeft, ArrowRight, ArrowUpDown,
    Plus, Minus, Search, Menu, More, Grip, Trash, Pencil, Copy, Save, Refresh, Download, Upload, Filter, Eye, Lock,
    Star, Folder, File, Home, ExternalLink, Play, Pause, Stop, Info, Alert, Settings, Calendar, Clock, User, Globe, Image, Cube, Cpu, Puzzle,
}

internal static class Glyphs
{
    // Polylines and convex fills on a 24-unit grid
    private sealed class Shape
    {
        public readonly List<Vector2[]> Strokes = new();
        public readonly List<Vector2[]> Fills = new();

        public Shape Line(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            Strokes.Add(pts);
            return this;
        }

        // Degrees, clockwise from pointing right
        public Shape Arc(float cx, float cy, float r, float from, float to)
        {
            int n = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(to - from) / 12f));
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(from, to, (float)i / n) * Mathf.Deg2Rad;
                pts[i] = new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a));
            }
            Strokes.Add(pts);
            return this;
        }

        public Shape Circle(float cx, float cy, float r) => Arc(cx, cy, r, 0f, 360f);

        public Shape Fill(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            Fills.Add(pts);
            return this;
        }

        public Shape Dot(float cx, float cy, float r = 1.4f)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a));
            }
            Fills.Add(pts);
            return this;
        }

        public Shape Rect(float x0, float y0, float x1, float y1) => Line(x0, y0, x1, y0, x1, y1, x0, y1, x0, y0);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Glyph, Shape> s_shapes = new();
    // Reused for every glyph. The backend copies the vertices when it draws
    [ThreadStatic] private static List<Vector2>? s_tris;

    // Centered in r, drawn as one batch of triangles
    public static void Paint(IPainter p, Rect r, Glyph glyph, Color c)
    {
        var shape = s_shapes.GetOrAdd(glyph, Build);
        float s = Mathf.Min(r.width, r.height) / 24f;
        if (s <= 0f) return;
        var origin = r.center - new Vector2(12f, 12f) * s;
        float half = Mathf.Max(1.2f, 2f * s) / 2f;
        Vector2 At(Vector2 v) => origin + v * s;
        var tris = s_tris ??= new List<Vector2>();
        tris.Clear();
        foreach (var poly in shape.Fills)
            for (int i = 1; i < poly.Length - 1; i++)
            {
                tris.Add(At(poly[0]));
                tris.Add(At(poly[i]));
                tris.Add(At(poly[i + 1]));
            }
        foreach (var line in shape.Strokes)
            for (int i = 1; i < line.Length; i++) AddSegment(tris, At(line[i - 1]), At(line[i]), half);
        if (tris.Count > 0) p.FillTriangles(tris, c);
    }

    // Two triangles per stroke. Runs half past each end to fill the joints between segments
    internal static void AddSegment(List<Vector2> tris, Vector2 a, Vector2 b, float half)
    {
        var d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        d = d.normalized * half;
        var n = new Vector2(-d.y, d.x);
        a -= d;
        b += d;
        tris.Add(a + n);
        tris.Add(b + n);
        tris.Add(b - n);
        tris.Add(a + n);
        tris.Add(b - n);
        tris.Add(a - n);
    }

    private static Shape Build(Glyph g)
    {
        var s = new Shape();
        switch (g)
        {
            case Glyph.Check: return s.Line(20, 6, 9, 17, 4, 12);
            case Glyph.X: return s.Line(18, 6, 6, 18).Line(6, 6, 18, 18);
            case Glyph.ChevronUp: return s.Line(6, 15, 12, 9, 18, 15);
            case Glyph.ChevronDown: return s.Line(6, 9, 12, 15, 18, 9);
            case Glyph.ChevronLeft: return s.Line(15, 18, 9, 12, 15, 6);
            case Glyph.ChevronRight: return s.Line(9, 18, 15, 12, 9, 6);
            case Glyph.ArrowUp: return s.Line(12, 19, 12, 5).Line(5, 12, 12, 5, 19, 12);
            case Glyph.ArrowDown: return s.Line(12, 5, 12, 19).Line(19, 12, 12, 19, 5, 12);
            case Glyph.ArrowLeft: return s.Line(19, 12, 5, 12).Line(12, 19, 5, 12, 12, 5);
            case Glyph.ArrowRight: return s.Line(5, 12, 19, 12).Line(12, 5, 19, 12, 12, 19);
            case Glyph.ArrowUpDown: return s.Line(3, 8, 7, 4, 11, 8).Line(7, 4, 7, 20).Line(21, 16, 17, 20, 13, 16).Line(17, 20, 17, 4);
            case Glyph.Plus: return s.Line(12, 5, 12, 19).Line(5, 12, 19, 12);
            case Glyph.Minus: return s.Line(5, 12, 19, 12);
            case Glyph.Search: return s.Circle(11, 11, 7).Line(21, 21, 16.2f, 16.2f);
            case Glyph.Menu: return s.Line(4, 6, 20, 6).Line(4, 12, 20, 12).Line(4, 18, 20, 18);
            case Glyph.More: return s.Dot(5, 12, 1.6f).Dot(12, 12, 1.6f).Dot(19, 12, 1.6f);
            case Glyph.Grip: return s.Dot(9, 5).Dot(15, 5).Dot(9, 12).Dot(15, 12).Dot(9, 19).Dot(15, 19);
            case Glyph.Trash:
                return s.Line(3, 6, 21, 6).Line(19, 6, 18, 21, 6, 21, 5, 6).Line(8, 6, 8, 3, 16, 3, 16, 6).Line(10, 11, 10, 17).Line(14, 11, 14, 17);
            case Glyph.Pencil: return s.Line(17, 3, 21, 7, 8, 20, 3, 21, 4, 16, 17, 3).Line(15, 5, 19, 9);
            case Glyph.Copy: return s.Rect(8, 8, 21, 21).Line(4, 16, 3, 16, 3, 3, 16, 3, 16, 4);
            case Glyph.Save: return s.Line(5, 3, 16, 3, 21, 8, 21, 21, 3, 21, 3, 3, 5, 3).Line(7, 3, 7, 8, 15, 8).Rect(7, 13, 17, 21);
            case Glyph.Refresh:
                return s.Arc(12, 12, 9, 180, 320).Line(18.9f, 6.2f, 21, 8).Line(21, 3, 21, 8, 16, 8)
                    .Arc(12, 12, 9, 0, 140).Line(5.1f, 17.8f, 3, 16).Line(8, 16, 3, 16, 3, 21);
            case Glyph.Download: return s.Line(21, 15, 21, 21, 3, 21, 3, 15).Line(7, 10, 12, 15, 17, 10).Line(12, 15, 12, 3);
            case Glyph.Upload: return s.Line(21, 15, 21, 21, 3, 21, 3, 15).Line(17, 8, 12, 3, 7, 8).Line(12, 3, 12, 15);
            case Glyph.Filter: return s.Line(22, 3, 2, 3, 10, 12.5f, 10, 19, 14, 21, 14, 12.5f, 22, 3);
            case Glyph.Eye:
                return s.Line(2, 12, 5, 8, 9, 5.5f, 12, 5, 15, 5.5f, 19, 8, 22, 12, 19, 16, 15, 18.5f, 12, 19, 9, 18.5f, 5, 16, 2, 12).Circle(12, 12, 3);
            case Glyph.Lock: return s.Rect(4, 11, 20, 21).Line(8, 11, 8, 7).Arc(12, 7, 4, 180, 360).Line(16, 7, 16, 11);
            case Glyph.Star:
                {
                    var xy = new float[22];
                    for (int i = 0; i <= 10; i++)
                    {
                        float a = (-90f + i * 36f) * Mathf.Deg2Rad, r = i % 2 == 0 ? 10f : 4.2f;
                        xy[i * 2] = 12f + r * Mathf.Cos(a);
                        xy[i * 2 + 1] = 12.8f + r * Mathf.Sin(a);
                    }
                    return s.Line(xy);
                }
            case Glyph.Folder: return s.Line(3, 20, 21, 20, 21, 8, 12, 8, 10, 5, 3, 5, 3, 20);
            case Glyph.File: return s.Line(14, 2, 5, 2, 5, 22, 19, 22, 19, 7, 14, 2).Line(14, 2, 14, 7, 19, 7);
            case Glyph.Home: return s.Line(3, 10, 12, 3, 21, 10).Line(5, 9, 5, 21, 19, 21, 19, 9).Line(10, 21, 10, 14, 14, 14, 14, 21);
            case Glyph.ExternalLink: return s.Line(15, 3, 21, 3, 21, 9).Line(10, 14, 21, 3).Line(18, 13, 18, 21, 3, 21, 3, 6, 11, 6);
            case Glyph.Play: return s.Fill(6, 4, 20, 12, 6, 20);
            case Glyph.Pause: return s.Fill(6, 4, 10, 4, 10, 20, 6, 20).Fill(14, 4, 18, 4, 18, 20, 14, 20);
            case Glyph.Stop: return s.Fill(5, 5, 19, 5, 19, 19, 5, 19);
            case Glyph.Info: return s.Circle(12, 12, 10).Line(12, 16, 12, 11).Dot(12, 7.8f);
            case Glyph.Alert: return s.Line(12, 3, 22, 20, 2, 20, 12, 3).Line(12, 9, 12, 13.5f).Dot(12, 16.8f);
            case Glyph.Settings:
                {
                    var xy = new float[66];
                    for (int i = 0; i <= 32; i++)
                    {
                        float a = i / 32f * Mathf.PI * 2f, r = (i / 2) % 2 == 0 ? 9.5f : 7f;
                        xy[i * 2] = 12f + r * Mathf.Cos(a);
                        xy[i * 2 + 1] = 12f + r * Mathf.Sin(a);
                    }
                    return s.Line(xy).Circle(12, 12, 3);
                }
            case Glyph.Calendar: return s.Rect(3, 5, 21, 21).Line(3, 10, 21, 10).Line(8, 3, 8, 7).Line(16, 3, 16, 7);
            case Glyph.Clock: return s.Circle(12, 12, 10).Line(12, 6, 12, 12, 16, 14);
            case Glyph.User: return s.Circle(12, 8, 4).Arc(12, 22, 8, 180, 360);
            case Glyph.Globe:
                return s.Circle(12, 12, 10).Line(2, 12, 22, 12)
                    .Line(12, 2, 8.5f, 7, 8, 12, 8.5f, 17, 12, 22).Line(12, 2, 15.5f, 7, 16, 12, 15.5f, 17, 12, 22);
            case Glyph.Image: return s.Rect(3, 4, 21, 20).Circle(8.5f, 9.5f, 1.5f).Line(3, 17, 9, 12, 13, 16, 16, 13, 21, 18);
            case Glyph.Cube:
                return s.Line(12, 2.5f, 20.5f, 7, 20.5f, 17, 12, 21.5f, 3.5f, 17, 3.5f, 7, 12, 2.5f)
                    .Line(3.5f, 7, 12, 12, 20.5f, 7).Line(12, 12, 12, 21.5f);
            case Glyph.Cpu:
                return s.Rect(5, 5, 19, 19).Rect(9, 9, 15, 15)
                    .Line(9, 2, 9, 5).Line(15, 2, 15, 5).Line(9, 19, 9, 22).Line(15, 19, 15, 22)
                    .Line(2, 9, 5, 9).Line(2, 15, 5, 15).Line(19, 9, 22, 9).Line(19, 15, 22, 15);
            case Glyph.Puzzle:
                return s.Line(4, 12.2f, 4, 8, 8.2f, 8).Arc(10, 6, 2.7f, 132, 408).Line(11.8f, 8, 16, 8, 16, 12.2f).Arc(18, 14, 2.7f, 222, 498)
                    .Line(16, 15.8f, 16, 20, 11.8f, 20).Arc(10, 22, 2.7f, 312, 228).Line(8.2f, 20, 4, 20, 4, 15.8f).Arc(2, 14, 2.7f, 42, -42);
            default: return s;
        }
    }
}
