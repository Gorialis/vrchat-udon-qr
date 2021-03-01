// -*- coding: utf-8 -*-
/*
MIT License

Copyright (c) 2021 Devon (Gorialis) R

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using UdonSharpEditor;
#endif

public class UdonQR : UdonSharpBehaviour
{
    // Udon does not support UTF-8 or expose System.Text.Encoding so we must implement this ourselves
    private short[] ToUTF8(char[] characters)
    {
        short[] buffer = new short[characters.Length * 4];

        int writeIndex = 0;
        for (int i = 0; i < characters.Length; i++)
        {
            uint character = characters[i];

            if (character < 0x80)
            {
                buffer[writeIndex++] = (short)character;
            } else if (character < 0x800)
            {
                buffer[writeIndex++] = (short)(0b11000000 | ((character >> 6) & 0b11111));
                buffer[writeIndex++] = (short)(0b10000000 | (character & 0b111111));
            } else if (character < 0x10000)
            {
                buffer[writeIndex++] = (short)(0b11100000 | ((character >> 12) & 0b1111));
                buffer[writeIndex++] = (short)(0b10000000 | ((character >> 6) & 0b111111));
                buffer[writeIndex++] = (short)(0b10000000 | (character & 0b111111));
            } else
            {
                buffer[writeIndex++] = (short)(0b11110000 | ((character >> 18) & 0b111));
                buffer[writeIndex++] = (short)(0b10000000 | ((character >> 12) & 0b111111));
                buffer[writeIndex++] = (short)(0b10000000 | ((character >> 6) & 0b111111));
                buffer[writeIndex++] = (short)(0b10000000 | (character & 0b111111));
            }
        }

        // We do this to truncate off the end of the array
        // This would be a lot easier with Array.Resize, but Udon once again does not allow access to it.
        short[] output = new short[writeIndex];

        for (int i = 0; i < writeIndex; i++)
            output[i] = buffer[i];
        
        return output;
    }

    public readonly int ERROR_CORRECTION_L = 1;
    public readonly int ERROR_CORRECTION_M = 0;
    public readonly int ERROR_CORRECTION_Q = 3;
    public readonly int ERROR_CORRECTION_H = 2;

    public const int MODE_NUMBER = 1 << 0;
    public const int MODE_ALPHANUMERIC = 1 << 1;
    public const int MODE_BYTES = 1 << 2;
    public const int MODE_KANJI = 1 << 3;


    private readonly int[][] ADJUST_PATTERN_POSITIONS = {
        new int[] {},
        new int[] {6, 18},
        new int[] {6, 22},
        new int[] {6, 26},
        new int[] {6, 30},
        new int[] {6, 34},
        new int[] {6, 22, 38},
        new int[] {6, 24, 42},
        new int[] {6, 26, 46},
        new int[] {6, 28, 50},
        new int[] {6, 30, 54},
        new int[] {6, 32, 58},
        new int[] {6, 34, 62},
        new int[] {6, 26, 46, 66},
        new int[] {6, 26, 48, 70},
        new int[] {6, 26, 50, 74},
        new int[] {6, 30, 54, 78},
        new int[] {6, 30, 56, 82},
        new int[] {6, 30, 58, 86},
        new int[] {6, 34, 62, 90},
        new int[] {6, 28, 50, 72, 94},
        new int[] {6, 26, 50, 74, 98},
        new int[] {6, 30, 54, 78, 102},
        new int[] {6, 28, 54, 80, 106},
        new int[] {6, 32, 58, 84, 110},
        new int[] {6, 30, 58, 86, 114},
        new int[] {6, 34, 62, 90, 118},
        new int[] {6, 26, 50, 74, 98, 122},
        new int[] {6, 30, 54, 78, 102, 126},
        new int[] {6, 26, 52, 78, 104, 130},
        new int[] {6, 30, 56, 82, 108, 134},
        new int[] {6, 34, 60, 86, 112, 138},
        new int[] {6, 30, 58, 86, 114, 142},
        new int[] {6, 34, 62, 90, 118, 146},
        new int[] {6, 30, 54, 78, 102, 126, 150},
        new int[] {6, 24, 50, 76, 102, 128, 154},
        new int[] {6, 28, 54, 80, 106, 132, 158},
        new int[] {6, 32, 58, 84, 110, 136, 162},
        new int[] {6, 26, 54, 82, 110, 138, 166},
        new int[] {6, 30, 58, 86, 114, 142, 170},
    };

    private readonly int[][] RS_BLOCKS = {
        // Version 1
        /* M */ new int[] {1, 26, 16}, /* L */ new int[] {1, 26, 19}, /* H */ new int[] {1, 26, 9}, /* Q */ new int[] {1, 26, 13},
        // Version 2
        /* M */ new int[] {1, 44, 28}, /* L */ new int[] {1, 44, 34}, /* H */ new int[] {1, 44, 16}, /* Q */ new int[] {1, 44, 22},
        // Version 3
        /* M */ new int[] {1, 70, 44}, /* L */ new int[] {1, 70, 55}, /* H */ new int[] {2, 35, 13}, /* Q */ new int[] {2, 35, 17},
        // Version 4
        /* M */ new int[] {2, 50, 32}, /* L */ new int[] {1, 100, 80}, /* H */ new int[] {4, 25, 9}, /* Q */ new int[] {2, 50, 24},
        // Version 5
        /* M */ new int[] {2, 67, 43}, /* L */ new int[] {1, 134, 108}, /* H */ new int[] {2, 33, 11, 2, 34, 12}, /* Q */ new int[] {2, 33, 15, 2, 34, 16},
        // Version 6
        /* M */ new int[] {4, 43, 27}, /* L */ new int[] {2, 86, 68}, /* H */ new int[] {4, 43, 15}, /* Q */ new int[] {4, 43, 19},
        // Version 7
        /* M */ new int[] {4, 49, 31}, /* L */ new int[] {2, 98, 78}, /* H */ new int[] {4, 39, 13, 1, 40, 14}, /* Q */ new int[] {2, 32, 14, 4, 33, 15},
        // Version 8
        /* M */ new int[] {2, 60, 38, 2, 61, 39}, /* L */ new int[] {2, 121, 97}, /* H */ new int[] {4, 40, 14, 2, 41, 15}, /* Q */ new int[] {4, 40, 18, 2, 41, 19},
        // Version 9
        /* M */ new int[] {3, 58, 36, 2, 59, 37}, /* L */ new int[] {2, 146, 116}, /* H */ new int[] {4, 36, 12, 4, 37, 13}, /* Q */ new int[] {4, 36, 16, 4, 37, 17},
        // Version 10
        /* M */ new int[] {4, 69, 43, 1, 70, 44}, /* L */ new int[] {2, 86, 68, 2, 87, 69}, /* H */ new int[] {6, 43, 15, 2, 44, 16}, /* Q */ new int[] {6, 43, 19, 2, 44, 20},
        // Version 11
        /* M */ new int[] {1, 80, 50, 4, 81, 51}, /* L */ new int[] {4, 101, 81}, /* H */ new int[] {3, 36, 12, 8, 37, 13}, /* Q */ new int[] {4, 50, 22, 4, 51, 23},
        // Version 12
        /* M */ new int[] {6, 58, 36, 2, 59, 37}, /* L */ new int[] {2, 116, 92, 2, 117, 93}, /* H */ new int[] {7, 42, 14, 4, 43, 15}, /* Q */ new int[] {4, 46, 20, 6, 47, 21},
        // Version 13
        /* M */ new int[] {8, 59, 37, 1, 60, 38}, /* L */ new int[] {4, 133, 107}, /* H */ new int[] {12, 33, 11, 4, 34, 12}, /* Q */ new int[] {8, 44, 20, 4, 45, 21},
        // Version 14
        /* M */ new int[] {4, 64, 40, 5, 65, 41}, /* L */ new int[] {3, 145, 115, 1, 146, 116}, /* H */ new int[] {11, 36, 12, 5, 37, 13}, /* Q */ new int[] {11, 36, 16, 5, 37, 17},
        // Version 15
        /* M */ new int[] {5, 65, 41, 5, 66, 42}, /* L */ new int[] {5, 109, 87, 1, 110, 88}, /* H */ new int[] {11, 36, 12, 7, 37, 13}, /* Q */ new int[] {5, 54, 24, 7, 55, 25},
        // Version 16
        /* M */ new int[] {7, 73, 45, 3, 74, 46}, /* L */ new int[] {5, 122, 98, 1, 123, 99}, /* H */ new int[] {3, 45, 15, 13, 46, 16}, /* Q */ new int[] {15, 43, 19, 2, 44, 20},
        // Version 17
        /* M */ new int[] {10, 74, 46, 1, 75, 47}, /* L */ new int[] {1, 135, 107, 5, 136, 108}, /* H */ new int[] {2, 42, 14, 17, 43, 15}, /* Q */ new int[] {1, 50, 22, 15, 51, 23},
        // Version 18
        /* M */ new int[] {9, 69, 43, 4, 70, 44}, /* L */ new int[] {5, 150, 120, 1, 151, 121}, /* H */ new int[] {2, 42, 14, 19, 43, 15}, /* Q */ new int[] {17, 50, 22, 1, 51, 23},
        // Version 19
        /* M */ new int[] {3, 70, 44, 11, 71, 45}, /* L */ new int[] {3, 141, 113, 4, 142, 114}, /* H */ new int[] {9, 39, 13, 16, 40, 14}, /* Q */ new int[] {17, 47, 21, 4, 48, 22},
        // Version 20
        /* M */ new int[] {3, 67, 41, 13, 68, 42}, /* L */ new int[] {3, 135, 107, 5, 136, 108}, /* H */ new int[] {15, 43, 15, 10, 44, 16}, /* Q */ new int[] {15, 54, 24, 5, 55, 25},
        // Version 21
        /* M */ new int[] {17, 68, 42}, /* L */ new int[] {4, 144, 116, 4, 145, 117}, /* H */ new int[] {19, 46, 16, 6, 47, 17}, /* Q */ new int[] {17, 50, 22, 6, 51, 23},
        // Version 22
        /* M */ new int[] {17, 74, 46}, /* L */ new int[] {2, 139, 111, 7, 140, 112}, /* H */ new int[] {34, 37, 13}, /* Q */ new int[] {7, 54, 24, 16, 55, 25},
        // Version 23
        /* M */ new int[] {4, 75, 47, 14, 76, 48}, /* L */ new int[] {4, 151, 121, 5, 152, 122}, /* H */ new int[] {16, 45, 15, 14, 46, 16}, /* Q */ new int[] {11, 54, 24, 14, 55, 25},
        // Version 24
        /* M */ new int[] {6, 73, 45, 14, 74, 46}, /* L */ new int[] {6, 147, 117, 4, 148, 118}, /* H */ new int[] {30, 46, 16, 2, 47, 17}, /* Q */ new int[] {11, 54, 24, 16, 55, 25},
        // Version 25
        /* M */ new int[] {8, 75, 47, 13, 76, 48}, /* L */ new int[] {8, 132, 106, 4, 133, 107}, /* H */ new int[] {22, 45, 15, 13, 46, 16}, /* Q */ new int[] {7, 54, 24, 22, 55, 25},
        // Version 26
        /* M */ new int[] {19, 74, 46, 4, 75, 47}, /* L */ new int[] {10, 142, 114, 2, 143, 115}, /* H */ new int[] {33, 46, 16, 4, 47, 17}, /* Q */ new int[] {28, 50, 22, 6, 51, 23},
        // Version 27
        /* M */ new int[] {22, 73, 45, 3, 74, 46}, /* L */ new int[] {8, 152, 122, 4, 153, 123}, /* H */ new int[] {12, 45, 15, 28, 46, 16}, /* Q */ new int[] {8, 53, 23, 26, 54, 24},
        // Version 28
        /* M */ new int[] {3, 73, 45, 23, 74, 46}, /* L */ new int[] {3, 147, 117, 10, 148, 118}, /* H */ new int[] {11, 45, 15, 31, 46, 16}, /* Q */ new int[] {4, 54, 24, 31, 55, 25},
        // Version 29
        /* M */ new int[] {21, 73, 45, 7, 74, 46}, /* L */ new int[] {7, 146, 116, 7, 147, 117}, /* H */ new int[] {19, 45, 15, 26, 46, 16}, /* Q */ new int[] {1, 53, 23, 37, 54, 24},
        // Version 30
        /* M */ new int[] {19, 75, 47, 10, 76, 48}, /* L */ new int[] {5, 145, 115, 10, 146, 116}, /* H */ new int[] {23, 45, 15, 25, 46, 16}, /* Q */ new int[] {15, 54, 24, 25, 55, 25},
        // Version 31
        /* M */ new int[] {2, 74, 46, 29, 75, 47}, /* L */ new int[] {13, 145, 115, 3, 146, 116}, /* H */ new int[] {23, 45, 15, 28, 46, 16}, /* Q */ new int[] {42, 54, 24, 1, 55, 25},
        // Version 32
        /* M */ new int[] {10, 74, 46, 23, 75, 47}, /* L */ new int[] {17, 145, 115}, /* H */ new int[] {19, 45, 15, 35, 46, 16}, /* Q */ new int[] {10, 54, 24, 35, 55, 25},
        // Version 33
        /* M */ new int[] {14, 74, 46, 21, 75, 47}, /* L */ new int[] {17, 145, 115, 1, 146, 116}, /* H */ new int[] {11, 45, 15, 46, 46, 16}, /* Q */ new int[] {29, 54, 24, 19, 55, 25},
        // Version 34
        /* M */ new int[] {14, 74, 46, 23, 75, 47}, /* L */ new int[] {13, 145, 115, 6, 146, 116}, /* H */ new int[] {59, 46, 16, 1, 47, 17}, /* Q */ new int[] {44, 54, 24, 7, 55, 25},
        // Version 35
        /* M */ new int[] {12, 75, 47, 26, 76, 48}, /* L */ new int[] {12, 151, 121, 7, 152, 122}, /* H */ new int[] {22, 45, 15, 41, 46, 16}, /* Q */ new int[] {39, 54, 24, 14, 55, 25},
        // Version 36
        /* M */ new int[] {6, 75, 47, 34, 76, 48}, /* L */ new int[] {6, 151, 121, 14, 152, 122}, /* H */ new int[] {2, 45, 15, 64, 46, 16}, /* Q */ new int[] {46, 54, 24, 10, 55, 25},
        // Version 37
        /* M */ new int[] {29, 74, 46, 14, 75, 47}, /* L */ new int[] {17, 152, 122, 4, 153, 123}, /* H */ new int[] {24, 45, 15, 46, 46, 16}, /* Q */ new int[] {49, 54, 24, 10, 55, 25},
        // Version 38
        /* M */ new int[] {13, 74, 46, 32, 75, 47}, /* L */ new int[] {4, 152, 122, 18, 153, 123}, /* H */ new int[] {42, 45, 15, 32, 46, 16}, /* Q */ new int[] {48, 54, 24, 14, 55, 25},
        // Version 39
        /* M */ new int[] {40, 75, 47, 7, 76, 48}, /* L */ new int[] {20, 147, 117, 4, 148, 118}, /* H */ new int[] {10, 45, 15, 67, 46, 16}, /* Q */ new int[] {43, 54, 24, 22, 55, 25},
        // Version 40
        /* M */ new int[] {18, 75, 47, 31, 76, 48}, /* L */ new int[] {19, 148, 118, 6, 149, 119}, /* H */ new int[] {20, 45, 15, 61, 46, 1}, /* Q */ new int[] {34, 54, 24, 34, 55, 25},
    };


    private readonly byte[] RS_POLYNOMIAL_LUT_KEYS = {
        7, 10, 13, 15, 16, 17, 18, 20, 22, 24, 26, 28, 30
    };
    private readonly byte[][] RS_POLYNOMIAL_LUT_VALUES = {
        new byte[] {1, 127, 122, 154, 164, 11, 68, 117},
        new byte[] {1, 216, 194, 159, 111, 199, 94, 95, 113, 157, 193},
        new byte[] {1, 137, 73, 227, 17, 177, 17, 52, 13, 46, 43, 83, 132, 120},
        new byte[] {1, 29, 196, 111, 163, 112, 74, 10, 105, 105, 139, 132, 151, 32, 134, 26},
        new byte[] {1, 59, 13, 104, 189, 68, 209, 30, 8, 163, 65, 41, 229, 98, 50, 36, 59},
        new byte[] {1, 119, 66, 83, 120, 119, 22, 197, 83, 249, 41, 143, 134, 85, 53, 125, 99, 79},
        new byte[] {1, 239, 251, 183, 113, 149, 175, 199, 215, 240, 220, 73, 82, 173, 75, 32, 67, 217, 146},
        new byte[] {1, 152, 185, 240, 5, 111, 99, 6, 220, 112, 150, 69, 36, 187, 22, 228, 198, 121, 121, 165, 174},
        new byte[] {1, 89, 179, 131, 176, 182, 244, 19, 189, 69, 40, 28, 137, 29, 123, 67, 253, 86, 218, 230, 26, 145, 245},
        new byte[] {1, 122, 118, 169, 70, 178, 237, 216, 102, 115, 150, 229, 73, 130, 72,61, 43, 206, 1, 237, 247, 127, 217, 144, 117},
        new byte[] {1, 246, 51, 183, 4, 136, 98, 199, 152, 77, 56, 206, 24, 145, 40, 209, 117, 233, 42, 135, 68, 70, 144, 146, 77, 43, 94},
        new byte[] {1, 252, 9, 28, 13, 18, 251, 208, 150, 103, 174, 100, 41, 167, 12, 247, 56, 117, 119, 233, 127, 181, 100, 121, 147, 176, 74, 58, 197},
        new byte[] {1, 212, 246, 77, 73, 195, 192, 75, 98, 5, 70, 103, 177, 22, 217, 138, 51, 181, 246, 72, 25, 18, 46, 228, 74, 216, 195, 11, 106, 130, 150},
    };

    private readonly int[][] BIT_LIMIT_TABLE = {
        new int[] {0, 128, 224, 352, 512, 688, 864, 992, 1232, 1456, 1728, 2032, 2320, 2672, 2920, 3320, 3624, 4056, 4504, 5016, 5352, 5712, 6256, 6880, 7312, 8000, 8496, 9024, 9544, 10136, 10984, 11640, 12328, 13048, 13800, 14496, 15312, 15936, 16816, 17728, 18672},
        new int[] {0, 152, 272, 440, 640, 864, 1088, 1248, 1552, 1856, 2192, 2592, 2960, 3424, 3688, 4184, 4712, 5176, 5768, 6360, 6888, 7456, 8048, 8752, 9392, 10208, 10960, 11744, 12248, 13048, 13880, 14744, 15640, 16568, 17528, 18448, 19472, 20528, 21616, 22496, 23648},
        new int[] {0, 72, 128, 208, 288, 368, 480, 528, 688, 800, 976, 1120, 1264, 1440, 1576, 1784, 2024, 2264, 2504, 2728, 3080, 3248, 3536, 3712, 4112, 4304, 4768, 5024, 5288, 5608, 5960, 6344, 6760, 7208, 7688, 7888, 8432, 8768, 9136, 9776, 10208},
        new int[] {0, 104, 176, 272, 384, 496, 608, 704, 880, 1056, 1232, 1440, 1648, 1952, 2088, 2360, 2600, 2936, 3176, 3560, 3880, 4096, 4544, 4912, 5312, 5744, 6032, 6464, 6968, 7288, 7880, 8264, 8920, 9368, 9848, 10288, 10832, 11408, 12016, 12656, 13328},
    };

    private const int BCH_G15 = (
        (1 << 10) | (1 << 8) | (1 << 5) | (1 << 4) | (1 << 2) | (1 << 1) | (1 << 0)
    );

    private const int BCH_G18 = (
        (1 << 12) | (1 << 11) | (1 << 10) | (1 << 9) | (1 << 8) | (1 << 5) | (1 << 2) | (1 << 0)
    );

    private const int BCH_G15_MASK = (
        (1 << 14) | (1 << 12) | (1 << 10) | (1 << 4) | (1 << 1)
    );

    private const string ALPHANUMERIC_LUT = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:'";

    // Populated in Start
    private readonly byte[] EXPONENT_TABLE = {
        1, 2, 4, 8, 16, 32, 64, 128, 29, 58, 116, 232, 205, 135, 19, 38, 76, 152, 45, 90, 180, 117, 234, 201, 143, 3, 6, 12, 24, 48, 96, 192, 157, 39, 78, 156, 37, 74, 148, 53, 106, 212, 181, 119, 238, 193, 159, 35, 70, 140, 5, 10, 20, 40, 80, 160, 93, 186, 105, 210, 185, 111, 222, 161, 95, 190, 97, 194, 153, 47, 94, 188, 101, 202, 137, 15, 30, 60, 120, 240, 253, 231, 211, 187, 107, 214, 177, 127, 254, 225, 223, 163, 91, 182, 113, 226, 217, 175, 67, 134, 17, 34, 68, 136, 13, 26, 52, 104, 208, 189, 103, 206, 129, 31, 62, 124, 248, 237, 199, 147, 59, 118, 236, 197, 151, 51, 102, 204, 133, 23, 46, 92, 184, 109, 218, 169, 79, 158, 33, 66, 132, 21, 42, 84, 168, 77, 154, 41, 82, 164, 85, 170, 73, 146, 57, 114, 228, 213, 183, 115, 230, 209, 191, 99, 198, 145, 63, 126, 252, 229, 215, 179, 123, 246, 241, 255, 227, 219, 171, 75, 150, 49, 98, 196, 149, 55, 110, 220, 165, 87, 174, 65, 130, 25, 50, 100, 200, 141, 7, 14, 28, 56, 112, 224, 221, 167, 83, 166, 81, 162, 89, 178, 121, 242, 249, 239, 195, 155, 43, 86, 172, 69, 138, 9, 18, 36, 72, 144, 61, 122, 244, 245, 247, 243, 251, 235, 203, 139, 11, 22, 44, 88, 176, 125, 250, 233, 207, 131, 27, 54, 108, 216, 173, 71, 142, 1
    };
    private readonly byte[] LOGARITHM_TABLE = {
        0, 0, 1, 25, 2, 50, 26, 198, 3, 223, 51, 238, 27, 104, 199, 75, 4, 100, 224, 14, 52, 141, 239, 129, 28, 193, 105, 248, 200, 8, 76, 113, 5, 138, 101, 47, 225, 36, 15, 33, 53, 147, 142, 218, 240, 18, 130, 69, 29, 181, 194, 125, 106, 39, 249, 185, 201, 154, 9, 120, 77, 228, 114, 166, 6, 191, 139, 98, 102, 221, 48, 253, 226, 152, 37, 179, 16, 145, 34, 136, 54, 208, 148, 206, 143, 150, 219, 189, 241, 210, 19, 92, 131, 56, 70, 64, 30, 66, 182, 163, 195, 72, 126, 110, 107, 58, 40, 84, 250, 133, 186, 61, 202, 94, 155, 159, 10, 21, 121, 43, 78, 212, 229, 172, 115, 243, 167, 87, 7, 112, 192, 247, 140, 128, 99, 13, 103, 74, 222, 237, 49, 197, 254, 24, 227, 165, 153, 119, 38, 184, 180, 124, 17, 68, 146, 217, 35, 32, 137, 46, 55, 63, 209, 91, 149, 188, 207, 205, 144, 135, 151, 178, 220, 252, 190, 97, 242, 86, 211, 171, 20, 42, 93, 158, 132, 60, 57, 83, 71, 109, 65, 162, 31, 45, 67, 216, 183, 123, 164, 118, 196, 23, 73, 236, 127, 12, 111, 246, 108, 161, 59, 82, 41, 157, 85, 170, 251, 96, 134, 177, 187, 204, 62, 90, 203, 89, 95, 176, 156, 169, 160, 81, 11, 245, 22, 235, 122, 117, 44, 215, 79, 174, 213, 233, 230, 231, 173, 232, 116, 214, 244, 234, 168, 80, 88, 175
    };

    public string Create(string input, int error_correction = 0, int mask_pattern = 1, string fill_character= "\u2588", string clear_character="\u2591")
    {
        // First, figure out what kind of encoding mode we want to use
        bool only_numeric = true;
        bool only_alphanumeric = true;

        for (int i = 0; i < input.Length; i++) {
            char c = input[i];

            if (c < 48 || c > 57)
                only_numeric = false;
            if (ALPHANUMERIC_LUT.IndexOf(c) == -1)
                only_alphanumeric = false;

            // If we know that we can't use a smaller encoding, there is no reason to continue,
            // because it won't change our minds about what encoding to use.
            if (!only_numeric && !only_alphanumeric)
                break;
        }

        int encoding_mode;
        short[] input_data;
        int input_stride;
        int input_stride_end;

        if (only_numeric) {
            int numeric_buffer_size = (input.Length / 3) + ((input.Length % 3 == 0) ? 0 : 1);
            input_data = new short[numeric_buffer_size];

            // Main buffer is 10 bits
            input_stride = 10;
            // End will be shorter if not all digits are used
            switch (input.Length % 3) {
                case 0:
                default:
                    input_stride_end = 10;
                    break;
                case 1:
                    input_stride_end = 4;
                    break;
                case 2:
                    input_stride_end = 7;
                    break;
            }

            for (int i = 0; i < numeric_buffer_size; i++) {
                string substring = input.Substring(i * 3, Mathf.Min(3, input.Length - (i * 3)));
                input_data[i] = Int16.Parse(substring);
            }

            encoding_mode = MODE_NUMBER;
        } else if (only_alphanumeric) {
            int alphanumeric_buffer_size = (input.Length / 2) + ((input.Length % 2 == 0) ? 0 : 1);
            input_data = new short[alphanumeric_buffer_size];

            // Main buffer is 11 bits
            input_stride = 11;
            // Only 6 bits are used if the final chunk is not a pair of 2
            input_stride_end = (input.Length % 2 == 0) ? 11 : 6;

            for (int i = 0; i < alphanumeric_buffer_size; i++) {
                string substring = input.Substring(i * 2, Mathf.Min(2, input.Length - (i * 2)));

                if (substring.Length == 2) {
                    input_data[i] = (short)((ALPHANUMERIC_LUT.IndexOf(substring[0]) * 45) + ALPHANUMERIC_LUT.IndexOf(substring[1]));
                } else {
                    input_data[i] = (short)ALPHANUMERIC_LUT.IndexOf(substring[0]);
                }
            }

            encoding_mode = MODE_ALPHANUMERIC;
        } else {
            input_data = ToUTF8(input.ToCharArray());
            // Always 8 bits
            input_stride = 8;
            input_stride_end = 8;

            encoding_mode = MODE_BYTES;
        }

        // Calculate best version based on whether the data fits
        int version;
        for (version = 1; version < 41; version++)
        {
            int ephem_mode_size;
            switch (encoding_mode)
            {
                case MODE_NUMBER:
                    ephem_mode_size = (version < 10) ? 10 : (version < 27) ? 12 : 14;
                    break;
                case MODE_ALPHANUMERIC:
                    ephem_mode_size = (version < 10) ? 9 : (version < 27) ? 11 : 13;
                    break;
                case MODE_BYTES:
                default:
                    ephem_mode_size = (version < 10) ? 8 : (version < 27) ? 16 : 16;
                    break;
                case MODE_KANJI:
                    ephem_mode_size = (version < 10) ? 8 : (version < 27) ? 10 : 12;
                    break;
            }

            int needed_bits = 4 + ephem_mode_size + ((input_data.Length - 1) * input_stride) + input_stride_end;
            int[] bit_limits = BIT_LIMIT_TABLE[error_correction];

            int bisector;
            for (bisector = version; bisector < bit_limits.Length; bisector++)
                if (bit_limits[bisector] >= needed_bits)
                    break;

            if (bisector == version)
                break;
        }

        int module_count = version * 4 + 17;
        // 0 for unset, 1 for fill, 2 for intentionally clear
        byte[] modules = new byte[module_count * module_count];

        // Set position patterns
        int edge_probe_position = module_count - 7;

        for (int y = 0; y < 9; y++) {
            for (int x = 0; x < 9; x++) {
                byte filled = (x >= 1 && x <= 7 && y >= 1 && y <= 7 && (x == 1 || x == 7 || y == 1 || y == 7 || (x >= 3 && x <= 5 && y >= 3 && y <= 5))) ? (byte)1 : (byte)2;
                if (x >= 1 && y >= 1)
                    modules[(module_count * (y - 1)) + (x - 1)] = filled;
                if (x <= 7 && y >= 1)
                    modules[(module_count * (y - 1)) + edge_probe_position + (x - 1)] = filled;
                if (x >= 1 && y <= 7)
                    modules[(module_count * (edge_probe_position + (y - 1))) + (x - 1)] = filled;
            }
        }

        // Set up adjust patterns
        int[] adjust_patterns = ADJUST_PATTERN_POSITIONS[version - 1];

        for (int y_index = 0; y_index < adjust_patterns.Length; y_index++) {
            int y_position = adjust_patterns[y_index];
            for (int x_index = 0; x_index < adjust_patterns.Length; x_index++) {
                int x_position = adjust_patterns[x_index];

                if (modules[(module_count * y_position) + x_position] != 0) continue;

                // Unrolled for optimization
                modules[(module_count * (y_position - 2)) + x_position - 2] = 1;
                modules[(module_count * (y_position - 2)) + x_position - 1] = 1;
                modules[(module_count * (y_position - 2)) + x_position + 0] = 1;
                modules[(module_count * (y_position - 2)) + x_position + 1] = 1;
                modules[(module_count * (y_position - 2)) + x_position + 2] = 1;

                modules[(module_count * (y_position - 1)) + x_position - 2] = 1;
                modules[(module_count * (y_position - 1)) + x_position - 1] = 2;
                modules[(module_count * (y_position - 1)) + x_position + 0] = 2;
                modules[(module_count * (y_position - 1)) + x_position + 1] = 2;
                modules[(module_count * (y_position - 1)) + x_position + 2] = 1;

                modules[(module_count * (y_position + 0)) + x_position - 2] = 1;
                modules[(module_count * (y_position + 0)) + x_position - 1] = 2;
                modules[(module_count * (y_position + 0)) + x_position + 0] = 1;
                modules[(module_count * (y_position + 0)) + x_position + 1] = 2;
                modules[(module_count * (y_position + 0)) + x_position + 2] = 1;

                modules[(module_count * (y_position + 1)) + x_position - 2] = 1;
                modules[(module_count * (y_position + 1)) + x_position - 1] = 2;
                modules[(module_count * (y_position + 1)) + x_position + 0] = 2;
                modules[(module_count * (y_position + 1)) + x_position + 1] = 2;
                modules[(module_count * (y_position + 1)) + x_position + 2] = 1;

                modules[(module_count * (y_position + 2)) + x_position - 2] = 1;
                modules[(module_count * (y_position + 2)) + x_position - 1] = 1;
                modules[(module_count * (y_position + 2)) + x_position + 0] = 1;
                modules[(module_count * (y_position + 2)) + x_position + 1] = 1;
                modules[(module_count * (y_position + 2)) + x_position + 2] = 1;
            }
        }

        // Set up timing pattern
        for (int i = 8; i < module_count - 8; i++) {
            if (modules[(module_count * i) + 6] == 0)
                modules[(module_count * i) + 6] = (i % 2 == 0) ? (byte)1 : (byte)2;
            if (modules[(module_count * 6) + i] == 0)
                modules[(module_count * 6) + i] = (i % 2 == 0) ? (byte)1 : (byte)2;
        }

        // Set up type info
        int bch_type_info = (error_correction << 3) | mask_pattern;
        int bch_type_info_d = bch_type_info << 10;

        int bch_type_digit_d = bch_type_info_d == 0 ? 0 : Mathf.FloorToInt(Mathf.Log(bch_type_info_d, 2.0f) + 1.0f);

        while (bch_type_digit_d - 11 >= 0) {
            bch_type_info_d ^= (BCH_G15 << (bch_type_digit_d - 11));
            bch_type_digit_d = bch_type_info_d == 0 ? 0 : Mathf.FloorToInt(Mathf.Log(bch_type_info_d, 2.0f) + 1.0f);
        }

        bch_type_info = ((bch_type_info << 10) | bch_type_info_d) ^ BCH_G15_MASK;

        for (int i = 0; i < 15; i++) {
            byte match = ((bch_type_info >> i) & 1) == 1 ? (byte)1 : (byte)2;

            // Vertical type info
            if (i < 6)
                modules[(module_count * i) + 8] = match;
            else if (i < 8)
                modules[(module_count * (i + 1)) + 8] = match;
            else
                modules[(module_count * (module_count - 15 + i)) + 8] = match;

            // Horizontal type info
            if (i < 8)
                modules[(module_count * 8) + module_count - i - 1] = match;
            else if (i < 9)
                modules[(module_count * 8) + 15 - i] = match;
            else
                modules[(module_count * 8) + 15 - i - 1] = match;
        }

        modules[(module_count * (module_count - 8)) + 8] = 1;

        if (version >= 7) {
            // Set up type number
            int bch_type_number = version << 12;

            bch_type_digit_d = bch_type_number == 0 ? 0 : Mathf.FloorToInt(Mathf.Log(bch_type_number, 2.0f) + 1.0f);

            while (bch_type_digit_d - 13 >= 0)
            {
                bch_type_number ^= (BCH_G18 << (bch_type_digit_d - 13));
                bch_type_digit_d = bch_type_number == 0 ? 0 : Mathf.FloorToInt(Mathf.Log(bch_type_number, 2.0f) + 1.0f);
            }

            bch_type_number = (version << 12) | bch_type_number;

            for (int i = 0; i < 18; i++)
            {
                byte match = ((bch_type_number >> i) & 1) == 1 ? (byte)1 : (byte)2;

                modules[(module_count * (i / 3)) + i % 3 + module_count - 8 - 3] = match;
                modules[(module_count * (i % 3 + module_count - 8 - 3)) + (i / 3)] = match;
            }
        }

        // Generate data cache
        byte[] singleton_data_cache = new byte[input_data.Length + 1024];
        int data_cache_bit_length = 4;

        // Write data mode
        singleton_data_cache[0] = (byte)(encoding_mode << 4);

        int mode_size;

        switch (encoding_mode) {
            case MODE_NUMBER:
                mode_size = (version < 10) ? 10 : (version < 27) ? 12 : 14;
                break;
            case MODE_ALPHANUMERIC:
                mode_size = (version < 10) ? 9 : (version < 27) ? 11 : 13;
                break;
            case MODE_BYTES:
            default:
                mode_size = (version < 10) ? 8 : (version < 27) ? 16 : 16;
                break;
            case MODE_KANJI:
                mode_size = (version < 10) ? 8 : (version < 27) ? 10 : 12;
                break;
        }

        // Write data length
        int write_size = mode_size;
        int source = (encoding_mode == MODE_BYTES) ? input_data.Length : input.Length;

        // write loop
        for (int write_index = 0; write_index < write_size; write_index++) {
            if (((source >> (write_size - write_index - 1)) & 1) == 1)
                singleton_data_cache[data_cache_bit_length / 8] |= (byte)(0x80 >> (data_cache_bit_length % 8));
            data_cache_bit_length++;
        }

        // Write actual data
        for (int i = 0; i < input_data.Length; i++) {
            write_size = (i == input_data.Length - 1) ? input_stride_end : input_stride;
            source = input_data[i];

            // write loop
            for (int write_index = 0; write_index < write_size; write_index++) {
                if (((source >> (write_size - write_index - 1)) & 1) == 1)
                    singleton_data_cache[data_cache_bit_length / 8] |= (byte)(0x80 >> (data_cache_bit_length % 8));
                data_cache_bit_length++;
            }
        }

        // Calculate RS blocks
        int[] rs_block = RS_BLOCKS[(version - 1) * 4 + error_correction];

        int[] rs_total_counts = new int[6 * 67];
        int[] rs_data_counts = new int[6 * 67];
        int rs_block_count = 0;
        int bit_limit = 0;
        int total_code_count = 0;

        for (int i = 0; i < rs_block.Length; i += 3) {
            int count = rs_block[i];
            int total_count = rs_block[i + 1];
            int data_count = rs_block[i + 2];

            for (int j = 0; j < count; j++, rs_block_count++) {
                rs_total_counts[rs_block_count] = total_count;
                rs_data_counts[rs_block_count] = data_count;
                total_code_count += total_count;
                bit_limit += data_count * 8;
            }
        }

        data_cache_bit_length += Mathf.Min(bit_limit - data_cache_bit_length, 4);

        // Delimit into words
        if (data_cache_bit_length % 8 != 0)
            data_cache_bit_length += (8 - (data_cache_bit_length % 8));

        // Pad the remaining space
        int bytes_to_fill = (bit_limit - data_cache_bit_length) / 8;
        for (int i = 0; i < bytes_to_fill; i++) {
            singleton_data_cache[data_cache_bit_length / 8] = (i % 2 == 0) ? (byte)0xEC : (byte)0x11;
            data_cache_bit_length += 8;
        }

        // Create bytes with RS blocks
        int dc_offset = 0;
        int max_dc_count = 0;
        int max_ec_count = 0;
        byte[][] dcdata = new byte[rs_block_count][];
        byte[][] ecdata = new byte[rs_block_count][];

        for (int r = 0; r < rs_block_count; r++) {
            int dc_count = rs_data_counts[r];
            int ec_count = rs_total_counts[r] - dc_count;

            max_dc_count = Mathf.Max(max_dc_count, dc_count);
            max_ec_count = Mathf.Max(max_ec_count, ec_count);

            dcdata[r] = new byte[dc_count];

            for (int i = 0; i < dc_count; i++)
                dcdata[r][i] = (byte)(0xFF & singleton_data_cache[i + dc_offset]);

            dc_offset += dc_count;

            // Error correction polynomial

            // I wish Udon supported Dictionary
            bool found = false;
            byte[] rs_polynomial = new byte[1];
            rs_polynomial[0] = 1;

            for (int i = 0; i < RS_POLYNOMIAL_LUT_KEYS.Length; i++) {
                if (ec_count == RS_POLYNOMIAL_LUT_KEYS[i]) {
                    found = true;
                    rs_polynomial = new byte[RS_POLYNOMIAL_LUT_VALUES[i].Length];
                    RS_POLYNOMIAL_LUT_VALUES[i].CopyTo(rs_polynomial, 0);
                    break;
                }
            }

            if (!found) {
                for (int i = 0; i < ec_count; i++) {
                    byte[] second = new byte[2];
                    second[0] = 1;
                    second[1] = EXPONENT_TABLE[i];

                    byte[] new_rs_polynomial = new byte[rs_polynomial.Length + second.Length - 1];

                    for (int a = 0; a < rs_polynomial.Length; a++) {
                        for (int b = 0; b < second.Length; b++) {
                            new_rs_polynomial[a + b] ^= EXPONENT_TABLE[(LOGARITHM_TABLE[rs_polynomial[a]] + LOGARITHM_TABLE[second[b]]) % 255];
                        }
                    }

                    rs_polynomial = new_rs_polynomial;
                    // Trim
                    int rs_trim_offset = 0;
                    for (; rs_trim_offset < rs_polynomial.Length; rs_trim_offset++)
                        if (rs_polynomial[rs_trim_offset] != 0)
                            break;

                    if (rs_trim_offset > 0) {
                        new_rs_polynomial = new byte[rs_polynomial.Length - rs_trim_offset];
                        
                        for (int j = rs_trim_offset; j < rs_polynomial.Length; j++)
                            new_rs_polynomial[j - rs_trim_offset] = rs_polynomial[j];

                        rs_polynomial = new_rs_polynomial;
                    }
                }
            }

            byte[] raw_polynomial = new byte[dcdata[r].Length + rs_polynomial.Length - 1];
            dcdata[r].CopyTo(raw_polynomial, 0);

            // Trim
            byte[] new_raw_polynomial;
            int offset = 0;
            for (; offset < raw_polynomial.Length; offset++)
                if (raw_polynomial[offset] != 0)
                    break;

            if (offset > 0) {
                new_raw_polynomial = new byte[raw_polynomial.Length - offset];
                
                for (int j = offset; j < raw_polynomial.Length; j++)
                    new_raw_polynomial[j - offset] = raw_polynomial[j];

                raw_polynomial = new_raw_polynomial;
            }

            byte[] modulo_polynomial;

            // Uh oh
            byte[] left_side = new byte[raw_polynomial.Length];
            raw_polynomial.CopyTo(left_side, 0);
            byte[] right_side = new byte[rs_polynomial.Length];
            rs_polynomial.CopyTo(right_side, 0);

            while (true) {
                if (left_side.Length - right_side.Length < 0) {
                    modulo_polynomial = left_side;
                    break;
                }
                
                int ratio = LOGARITHM_TABLE[left_side[0]] - LOGARITHM_TABLE[right_side[0]];
                byte[] output_polynomial = new byte[left_side.Length];

                for (int i = 0; i < right_side.Length; i++)
                    output_polynomial[i] = (byte)(left_side[i] ^ EXPONENT_TABLE[(LOGARITHM_TABLE[right_side[i]] + ratio) % 255]);

                for (int i = right_side.Length; i < left_side.Length; i++)
                    output_polynomial[i] = left_side[i];

                // Trim
                byte[] new_left_polynomial;
                offset = 0;
                for (; offset < output_polynomial.Length; offset++)
                    if (output_polynomial[offset] != 0)
                        break;

                if (offset > 0) {
                    new_left_polynomial = new byte[output_polynomial.Length - offset];
                    
                    for (int j = offset; j < output_polynomial.Length; j++)
                        new_left_polynomial[j - offset] = output_polynomial[j];

                    output_polynomial = new_left_polynomial;
                }

                left_side = output_polynomial;
            }

            ecdata[r] = new byte[rs_polynomial.Length - 1];

            for (int i = 0; i < rs_polynomial.Length - 1; i++) {
                int mod_index = i + modulo_polynomial.Length - ecdata[r].Length;

                ecdata[r][i] = (byte)(mod_index >= 0 ? modulo_polynomial[mod_index] : 0);
            }
            
        }

        byte[] data_cache = new byte[total_code_count];
        int data_cache_index = 0;

        for (int i = 0; i < max_dc_count; i++) {
            for (int r = 0; r < rs_block_count; r++) {
                if (i < dcdata[r].Length) {
                    data_cache[data_cache_index] = dcdata[r][i];
                    data_cache_index++;
                }
            }
        }

        for (int i = 0; i < max_ec_count; i++) {
            for (int r = 0; r < rs_block_count; r++) {
                if (i < ecdata[r].Length) {
                    data_cache[data_cache_index] = ecdata[r][i];
                    data_cache_index++;
                }
            }
        }

        // Map data

        int inc = -1;
        int row = module_count - 1;
        int bitIndex = 7;
        int byteIndex = 0;

        for (int col = module_count - 1; col > 0; col -= 2) {
            if (col <= 6) col--;

            while (true) {
                for (int c = 0; c < 2; c++) {
                    if (modules[(module_count * row) + col - c] == 0) {
                        bool dark = false;

                        if (byteIndex < data_cache.Length)
                            dark = (((data_cache[byteIndex] >> bitIndex) & 1) == 1);

                        switch (mask_pattern) {
                            case 0:
                                dark = ((row + (col - c)) % 2 == 0) ? !dark : dark;
                                break;
                            case 1:
                                dark = (row % 2 == 0) ? !dark : dark;
                                break;
                            case 2:
                                dark = ((col - c) % 3 == 0) ? !dark : dark;
                                break;
                            case 3:
                                dark = ((row + (col - c)) % 3 == 0) ? !dark : dark;
                                break;
                            case 4:
                                dark = (((row / 2) + ((col - c) / 3)) % 2 == 0) ? !dark : dark;
                                break;
                            case 5:
                                dark = ((row * (col - c)) % 2 + (row * (col - c)) % 3 == 0) ? !dark : dark;
                                break;
                            case 6:
                                dark = (((row * (col - c)) % 2 + (row * (col - c)) % 3) % 2 == 0) ? !dark : dark;
                                break;
                            case 7:
                                dark = (((row * (col - c)) % 3 + (row + (col - c)) % 2) % 2 == 0) ? !dark : dark;
                                break;
                        }

                        modules[(module_count * row) + col - c] = dark ? (byte)1 : (byte)2;
                        bitIndex -= 1;

                        if (bitIndex == -1) {
                            byteIndex += 1;
                            bitIndex = 7;
                        }
                    }
                }

                row += inc;

                if (row < 0 || module_count <= row) {
                    row -= inc;
                    inc = -inc;
                    break;
                }
            }
        }

        // Convert modules to text
        string output = "";

        for (int y = 0; y < module_count; ++y) {
            for (int x = 0; x < module_count; ++x)
                output += (modules[(y * module_count) + x] == 1) ? fill_character : clear_character;
            output += "\n";
        }

        return output;
    }

    void Start()
    {
        
    }
}

#if !COMPILER_UDONSHARP && UNITY_EDITOR

[CustomEditor(typeof(UdonQR))]
public class UdonQREditor : Editor
{
    private string inputString = "";

    private Text text;

    public override void OnInspectorGUI()
    {
        // Draws the default convert to UdonBehaviour button, program asset field, sync settings, etc.
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        UdonQR inspectorBehaviour = (UdonQR)target;

        if (!EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Enter play mode to run tests", MessageType.Info);

        EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);

        EditorGUILayout.LabelField("Input for QR");
        inputString = EditorGUILayout.TextArea(inputString);

        text = (Text)EditorGUILayout.ObjectField(text, typeof(Text), true);

        if (GUILayout.Button("Show QR"))
        {
            string output = inspectorBehaviour.Create(inputString);
            EditorUtility.DisplayDialog("Results", output, "OK");
            text.text = output;
        }

        EditorGUI.EndDisabledGroup();
    }
}
#endif
