using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GodjiVpn.Utils;

/// <summary>
/// Генератор QR-кода (ISO/IEC 18004) без внешних зависимостей — для QR реферальной ссылки
/// (порт 79b3e4b, в Android там zxing core). Байтовый режим UTF-8, уровень коррекции M,
/// версия 1–40 подбирается по длине, маска — по штрафным правилам стандарта. Алгоритм — по
/// эталонной реализации Nayuki (MIT).
/// </summary>
public static class QrCode
{
    private enum Ecc { L = 0, M = 1, Q = 2, H = 3 }

    private static readonly int[] FormatBitsOf = { 1, 0, 3, 2 }; // L, M, Q, H

    private static readonly sbyte[,] EccCodewordsPerBlock =
    {
        { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
        { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
    };

    private static readonly sbyte[,] NumErrorCorrectionBlocks =
    {
        { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
        { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
        { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
        { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 },
    };

    /// <summary>Матрица модулей [y, x], true — тёмный. Без "тихой зоны".</summary>
    public static bool[,] Encode(string text)
    {
        const Ecc ecl = Ecc.M;
        var data = Encoding.UTF8.GetBytes(text);

        int version = 1, dataCapacityBits = 0;
        for (; version <= 40; version++)
        {
            dataCapacityBits = GetNumDataCodewords(version, ecl) * 8;
            var ccBits = version <= 9 ? 8 : 16;
            if (data.Length < (1 << ccBits) && 4 + ccBits + data.Length * 8 <= dataCapacityBits) break;
        }
        if (version > 40) throw new ArgumentException("Слишком длинный текст для QR-кода");

        // Битовый поток: режим "байты" + длина + данные + терминатор + выравнивание + заполнитель.
        var bits = new List<bool>();
        void Append(int value, int count) { for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Append(0x4, 4);
        Append(data.Length, version <= 9 ? 8 : 16);
        foreach (var b in data) Append(b, 8);
        Append(0, Math.Min(4, dataCapacityBits - bits.Count));
        Append(0, (8 - bits.Count % 8) % 8);
        for (var pad = 0xEC; bits.Count < dataCapacityBits; pad ^= 0xEC ^ 0x11) Append(pad, 8);

        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        var allCodewords = AddEccAndInterleave(codewords, version, ecl);

        var size = version * 4 + 17;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];
        DrawFunctionPatterns(modules, isFunction, version, ecl);
        DrawCodewords(modules, isFunction, allCodewords);

        // Лучшая маска — с минимальным штрафом.
        var bestMask = 0;
        var minPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask);
            DrawFormatBits(modules, isFunction, ecl, mask);
            var penalty = GetPenaltyScore(modules);
            if (penalty < minPenalty) { minPenalty = penalty; bestMask = mask; }
            ApplyMask(modules, isFunction, mask); // XOR — повторное применение снимает маску
        }
        ApplyMask(modules, isFunction, bestMask);
        DrawFormatBits(modules, isFunction, ecl, bestMask);
        return modules;
    }

    /// <summary>Готовая картинка: чёрные модули на белом с тихой зоной 4 модуля.</summary>
    public static ImageSource ToImage(bool[,] modules, int quietZone = 4)
    {
        var size = modules.GetLength(0);
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (modules[y, x])
                    geometry.Children.Add(new RectangleGeometry(new Rect(x + quietZone, y + quietZone, 1, 1)));
        var total = size + quietZone * 2;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, total, total))));
        group.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static int GetNumRawDataModules(int ver)
    {
        var result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            var numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    private static int GetNumDataCodewords(int ver, Ecc ecl) =>
        GetNumRawDataModules(ver) / 8 - EccCodewordsPerBlock[(int)ecl, ver] * NumErrorCorrectionBlocks[(int)ecl, ver];

    private static byte[] AddEccAndInterleave(byte[] data, int ver, Ecc ecl)
    {
        int numBlocks = NumErrorCorrectionBlocks[(int)ecl, ver];
        int blockEccLen = EccCodewordsPerBlock[(int)ecl, ver];
        var rawCodewords = GetNumRawDataModules(ver) / 8;
        var numShortBlocks = numBlocks - rawCodewords % numBlocks;
        var shortBlockLen = rawCodewords / numBlocks;

        var divisor = ReedSolomonComputeDivisor(blockEccLen);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            var datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
            var dat = data.AsSpan(k, datLen).ToArray();
            k += datLen;
            var ecc = ReedSolomonComputeRemainder(dat, divisor);
            var block = new byte[shortBlockLen + 1];
            Array.Copy(dat, block, datLen);
            // Короткие блоки: пустое место на позиции datLen, чтобы все блоки были одной длины.
            Array.Copy(ecc, 0, block, i < numShortBlocks ? datLen + 1 : datLen, ecc.Length);
            blocks.Add(block);
        }

        var result = new List<byte>(rawCodewords);
        for (var i = 0; i < blocks[0].Length; i++)
            for (var j = 0; j < blocks.Count; j++)
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    result.Add(blocks[j][i]);
        return result.ToArray();
    }

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = (byte)Multiply(result[j], root);
                if (j + 1 < result.Length) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
                result[i] ^= (byte)Multiply(divisor[i], factor);
        }
        return result;
    }

    private static int Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z;
    }

    private static void DrawFunctionPatterns(bool[,] m, bool[,] f, int ver, Ecc ecl)
    {
        var size = m.GetLength(0);
        for (var i = 0; i < size; i++)
        {
            Set(m, f, 6, i, i % 2 == 0);
            Set(m, f, i, 6, i % 2 == 0);
        }
        DrawFinder(m, f, 3, 3);
        DrawFinder(m, f, size - 4, 3);
        DrawFinder(m, f, 3, size - 4);

        var align = GetAlignmentPatternPositions(ver);
        var n = align.Length;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        Set(m, f, align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

        DrawFormatBits(m, f, ecl, 0); // резервирует место; реальные биты — после выбора маски
        if (ver >= 7)
        {
            var rem = ver;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (ver << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(m, f, a, b, bit);
                Set(m, f, b, a, bit);
            }
        }
    }

    private static void DrawFinder(bool[,] m, bool[,] f, int x, int y)
    {
        var size = m.GetLength(0);
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx < 0 || xx >= size || yy < 0 || yy >= size) continue;
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                Set(m, f, xx, yy, dist != 2 && dist != 4);
            }
    }

    private static int[] GetAlignmentPatternPositions(int ver)
    {
        if (ver == 1) return Array.Empty<int>();
        var numAlign = ver / 7 + 2;
        var step = ver == 32 ? 26 : (ver * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
        var result = new int[numAlign];
        result[0] = 6;
        for (int i = numAlign - 1, pos = ver * 4 + 17 - 7; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    private static void DrawFormatBits(bool[,] m, bool[,] f, Ecc ecl, int mask)
    {
        var size = m.GetLength(0);
        var data = (FormatBitsOf[(int)ecl] << 3) | mask;
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;

        for (var i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(i));
        Set(m, f, 8, 7, Bit(6));
        Set(m, f, 8, 8, Bit(7));
        Set(m, f, 7, 8, Bit(8));
        for (var i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(i));

        for (var i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(i));
        Set(m, f, 8, size - 8, true); // всегда тёмный модуль
    }

    private static void Set(bool[,] m, bool[,] f, int x, int y, bool dark)
    {
        m[y, x] = dark;
        f[y, x] = true;
    }

    private static void DrawCodewords(bool[,] m, bool[,] f, byte[] data)
    {
        var size = m.GetLength(0);
        var i = 0;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (!f[y, x] && i < data.Length * 8)
                    {
                        m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    private static void ApplyMask(bool[,] m, bool[,] f, int mask)
    {
        var size = m.GetLength(0);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (f[y, x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    /// <summary>Штраф маски (правила N1–N4 стандарта, упрощённо для N3).</summary>
    private static int GetPenaltyScore(bool[,] m)
    {
        var size = m.GetLength(0);
        var result = 0;

        // N1: серии одного цвета длиной ≥5 в строках и столбцах.
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < size; a++)
            {
                var run = 1;
                for (var b = 1; b < size; b++)
                {
                    bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev) run++;
                    else { if (run >= 5) result += 3 + run - 5; run = 1; }
                }
                if (run >= 5) result += 3 + run - 5;
            }

        // N2: блоки 2×2 одного цвета.
        for (var y = 0; y < size - 1; y++)
            for (var x = 0; x < size - 1; x++)
            {
                var c = m[y, x];
                if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) result += 3;
            }

        // N3: узор 1:1:3:1:1 с четырьмя светлыми модулями с одной стороны.
        bool[] p1 = { true, false, true, true, true, false, true, false, false, false, false };
        bool[] p2 = { false, false, false, false, true, false, true, true, true, false, true };
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < size; a++)
                for (var b = 0; b + 11 <= size; b++)
                {
                    bool m1 = true, m2 = true;
                    for (var k = 0; k < 11 && (m1 || m2); k++)
                    {
                        var v = pass == 0 ? m[a, b + k] : m[b + k, a];
                        if (v != p1[k]) m1 = false;
                        if (v != p2[k]) m2 = false;
                    }
                    if (m1) result += 40;
                    if (m2) result += 40;
                }

        // N4: доля тёмных модулей далека от 50%.
        var dark = 0;
        foreach (var v in m) if (v) dark++;
        var total = size * size;
        var k4 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        result += Math.Max(0, k4) * 10;
        return result;
    }
}
