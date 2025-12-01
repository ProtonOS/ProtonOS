// ProtonOS kernel - PAL String Conversion APIs
// Win32-compatible string conversion functions for UTF-8/UTF-16 interop.
// Required for CoreCLR/RyuJIT integration.

using System.Runtime.InteropServices;

namespace ProtonOS.PAL;

/// <summary>
/// Code page constants.
/// </summary>
public static class CodePage
{
    public const uint CP_ACP = 0;           // Default ANSI code page
    public const uint CP_UTF7 = 65000;      // UTF-7
    public const uint CP_UTF8 = 65001;      // UTF-8
}

/// <summary>
/// MultiByteToWideChar flags.
/// </summary>
public static class MbFlags
{
    public const uint MB_PRECOMPOSED = 0x00000001;
    public const uint MB_COMPOSITE = 0x00000002;
    public const uint MB_USEGLYPHCHARS = 0x00000004;
    public const uint MB_ERR_INVALID_CHARS = 0x00000008;
}

/// <summary>
/// WideCharToMultiByte flags.
/// </summary>
public static class WcFlags
{
    public const uint WC_COMPOSITECHECK = 0x00000200;
    public const uint WC_DISCARDNS = 0x00000010;
    public const uint WC_SEPCHARS = 0x00000020;
    public const uint WC_DEFAULTCHAR = 0x00000040;
    public const uint WC_ERR_INVALID_CHARS = 0x00000080;
    public const uint WC_NO_BEST_FIT_CHARS = 0x00000400;
}

/// <summary>
/// CPINFO structure - compatible with Win32 CPINFO.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CpInfo
{
    public uint MaxCharSize;        // Max bytes per character
    public fixed byte DefaultChar[2];  // Default character
    public fixed byte LeadByte[12];    // Lead byte ranges (pairs, null-terminated)
}

/// <summary>
/// PAL String Conversion APIs - Win32-compatible MultiByteToWideChar and WideCharToMultiByte.
/// We assume UTF-8 as the default code page (CP_ACP = UTF-8).
/// </summary>
public static unsafe class StringApi
{
    /// <summary>
    /// Convert a multibyte (UTF-8) string to a wide (UTF-16) string.
    /// </summary>
    /// <param name="codePage">Code page to use (CP_UTF8 or CP_ACP)</param>
    /// <param name="dwFlags">Conversion flags</param>
    /// <param name="lpMultiByteStr">Pointer to multibyte string</param>
    /// <param name="cbMultiByte">Size in bytes (-1 for null-terminated)</param>
    /// <param name="lpWideCharStr">Pointer to wide string buffer (null to query size)</param>
    /// <param name="cchWideChar">Size of wide buffer in characters</param>
    /// <returns>Number of wide characters written/required, or 0 on error</returns>
    public static int MultiByteToWideChar(
        uint codePage,
        uint dwFlags,
        byte* lpMultiByteStr,
        int cbMultiByte,
        char* lpWideCharStr,
        int cchWideChar)
    {
        if (lpMultiByteStr == null)
            return 0;

        // Normalize code page - treat CP_ACP as UTF-8
        if (codePage == CodePage.CP_ACP)
            codePage = CodePage.CP_UTF8;

        // We only support UTF-8
        if (codePage != CodePage.CP_UTF8)
            return 0;

        // If cbMultiByte is -1, calculate length including null terminator
        int srcLen;
        if (cbMultiByte == -1)
        {
            srcLen = 0;
            while (lpMultiByteStr[srcLen] != 0)
                srcLen++;
            srcLen++; // Include null terminator
        }
        else
        {
            srcLen = cbMultiByte;
        }

        // First pass: calculate required size
        int requiredChars = 0;
        int srcPos = 0;

        while (srcPos < srcLen)
        {
            byte b = lpMultiByteStr[srcPos];

            // Check for null terminator when processing null-terminated string
            if (cbMultiByte == -1 && b == 0)
            {
                requiredChars++; // Count the null terminator
                break;
            }

            int codePoint;
            int bytesConsumed;

            if ((b & 0x80) == 0)
            {
                // ASCII: 0xxxxxxx
                codePoint = b;
                bytesConsumed = 1;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                // 2-byte: 110xxxxx 10xxxxxx
                if (srcPos + 1 >= srcLen)
                {
                    if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                        return 0;
                    codePoint = 0xFFFD; // Replacement character
                    bytesConsumed = 1;
                }
                else
                {
                    byte b2 = lpMultiByteStr[srcPos + 1];
                    if ((b2 & 0xC0) != 0x80)
                    {
                        if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                            return 0;
                        codePoint = 0xFFFD;
                        bytesConsumed = 1;
                    }
                    else
                    {
                        codePoint = ((b & 0x1F) << 6) | (b2 & 0x3F);
                        bytesConsumed = 2;
                    }
                }
            }
            else if ((b & 0xF0) == 0xE0)
            {
                // 3-byte: 1110xxxx 10xxxxxx 10xxxxxx
                if (srcPos + 2 >= srcLen)
                {
                    if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                        return 0;
                    codePoint = 0xFFFD;
                    bytesConsumed = 1;
                }
                else
                {
                    byte b2 = lpMultiByteStr[srcPos + 1];
                    byte b3 = lpMultiByteStr[srcPos + 2];
                    if ((b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80)
                    {
                        if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                            return 0;
                        codePoint = 0xFFFD;
                        bytesConsumed = 1;
                    }
                    else
                    {
                        codePoint = ((b & 0x0F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F);
                        bytesConsumed = 3;
                    }
                }
            }
            else if ((b & 0xF8) == 0xF0)
            {
                // 4-byte: 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
                if (srcPos + 3 >= srcLen)
                {
                    if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                        return 0;
                    codePoint = 0xFFFD;
                    bytesConsumed = 1;
                }
                else
                {
                    byte b2 = lpMultiByteStr[srcPos + 1];
                    byte b3 = lpMultiByteStr[srcPos + 2];
                    byte b4 = lpMultiByteStr[srcPos + 3];
                    if ((b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80 || (b4 & 0xC0) != 0x80)
                    {
                        if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                            return 0;
                        codePoint = 0xFFFD;
                        bytesConsumed = 1;
                    }
                    else
                    {
                        codePoint = ((b & 0x07) << 18) | ((b2 & 0x3F) << 12) | ((b3 & 0x3F) << 6) | (b4 & 0x3F);
                        bytesConsumed = 4;
                    }
                }
            }
            else
            {
                // Invalid UTF-8 lead byte
                if ((dwFlags & MbFlags.MB_ERR_INVALID_CHARS) != 0)
                    return 0;
                codePoint = 0xFFFD;
                bytesConsumed = 1;
            }

            // Count UTF-16 code units needed
            if (codePoint <= 0xFFFF)
            {
                requiredChars++;
            }
            else if (codePoint <= 0x10FFFF)
            {
                requiredChars += 2; // Surrogate pair
            }
            else
            {
                requiredChars++; // Replacement character
            }

            srcPos += bytesConsumed;
        }

        // If lpWideCharStr is null, just return required size
        if (lpWideCharStr == null || cchWideChar == 0)
            return requiredChars;

        // Check if buffer is large enough
        if (cchWideChar < requiredChars)
            return 0; // Buffer too small

        // Second pass: actually convert
        srcPos = 0;
        int dstPos = 0;

        while (srcPos < srcLen && dstPos < cchWideChar)
        {
            byte b = lpMultiByteStr[srcPos];

            if (cbMultiByte == -1 && b == 0)
            {
                lpWideCharStr[dstPos++] = '\0';
                break;
            }

            int codePoint;
            int bytesConsumed;

            if ((b & 0x80) == 0)
            {
                codePoint = b;
                bytesConsumed = 1;
            }
            else if ((b & 0xE0) == 0xC0 && srcPos + 1 < srcLen)
            {
                byte b2 = lpMultiByteStr[srcPos + 1];
                if ((b2 & 0xC0) == 0x80)
                {
                    codePoint = ((b & 0x1F) << 6) | (b2 & 0x3F);
                    bytesConsumed = 2;
                }
                else
                {
                    codePoint = 0xFFFD;
                    bytesConsumed = 1;
                }
            }
            else if ((b & 0xF0) == 0xE0 && srcPos + 2 < srcLen)
            {
                byte b2 = lpMultiByteStr[srcPos + 1];
                byte b3 = lpMultiByteStr[srcPos + 2];
                if ((b2 & 0xC0) == 0x80 && (b3 & 0xC0) == 0x80)
                {
                    codePoint = ((b & 0x0F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F);
                    bytesConsumed = 3;
                }
                else
                {
                    codePoint = 0xFFFD;
                    bytesConsumed = 1;
                }
            }
            else if ((b & 0xF8) == 0xF0 && srcPos + 3 < srcLen)
            {
                byte b2 = lpMultiByteStr[srcPos + 1];
                byte b3 = lpMultiByteStr[srcPos + 2];
                byte b4 = lpMultiByteStr[srcPos + 3];
                if ((b2 & 0xC0) == 0x80 && (b3 & 0xC0) == 0x80 && (b4 & 0xC0) == 0x80)
                {
                    codePoint = ((b & 0x07) << 18) | ((b2 & 0x3F) << 12) | ((b3 & 0x3F) << 6) | (b4 & 0x3F);
                    bytesConsumed = 4;
                }
                else
                {
                    codePoint = 0xFFFD;
                    bytesConsumed = 1;
                }
            }
            else
            {
                codePoint = 0xFFFD;
                bytesConsumed = 1;
            }

            // Write UTF-16
            if (codePoint <= 0xFFFF)
            {
                lpWideCharStr[dstPos++] = (char)codePoint;
            }
            else if (codePoint <= 0x10FFFF)
            {
                // Surrogate pair
                codePoint -= 0x10000;
                if (dstPos + 1 < cchWideChar)
                {
                    lpWideCharStr[dstPos++] = (char)(0xD800 | (codePoint >> 10));
                    lpWideCharStr[dstPos++] = (char)(0xDC00 | (codePoint & 0x3FF));
                }
                else
                {
                    return 0; // Buffer too small for surrogate pair
                }
            }
            else
            {
                lpWideCharStr[dstPos++] = (char)0xFFFD;
            }

            srcPos += bytesConsumed;
        }

        return dstPos;
    }

    /// <summary>
    /// Convert a wide (UTF-16) string to a multibyte (UTF-8) string.
    /// </summary>
    /// <param name="codePage">Code page to use (CP_UTF8 or CP_ACP)</param>
    /// <param name="dwFlags">Conversion flags</param>
    /// <param name="lpWideCharStr">Pointer to wide string</param>
    /// <param name="cchWideChar">Number of wide characters (-1 for null-terminated)</param>
    /// <param name="lpMultiByteStr">Pointer to multibyte buffer (null to query size)</param>
    /// <param name="cbMultiByte">Size of multibyte buffer in bytes</param>
    /// <param name="lpDefaultChar">Default character for unmappable chars (ignored for UTF-8)</param>
    /// <param name="lpUsedDefaultChar">Output: whether default char was used (ignored for UTF-8)</param>
    /// <returns>Number of bytes written/required, or 0 on error</returns>
    public static int WideCharToMultiByte(
        uint codePage,
        uint dwFlags,
        char* lpWideCharStr,
        int cchWideChar,
        byte* lpMultiByteStr,
        int cbMultiByte,
        byte* lpDefaultChar,
        int* lpUsedDefaultChar)
    {
        if (lpWideCharStr == null)
            return 0;

        // Normalize code page
        if (codePage == CodePage.CP_ACP)
            codePage = CodePage.CP_UTF8;

        // We only support UTF-8
        if (codePage != CodePage.CP_UTF8)
            return 0;

        // Clear used default char flag (UTF-8 doesn't use it)
        if (lpUsedDefaultChar != null)
            *lpUsedDefaultChar = 0;

        // If cchWideChar is -1, calculate length including null terminator
        int srcLen;
        if (cchWideChar == -1)
        {
            srcLen = 0;
            while (lpWideCharStr[srcLen] != 0)
                srcLen++;
            srcLen++; // Include null terminator
        }
        else
        {
            srcLen = cchWideChar;
        }

        // First pass: calculate required size
        int requiredBytes = 0;
        int srcPos = 0;

        while (srcPos < srcLen)
        {
            char c = lpWideCharStr[srcPos];

            // Check for null terminator
            if (cchWideChar == -1 && c == 0)
            {
                requiredBytes++; // Null terminator
                break;
            }

            int codePoint;

            // Handle surrogate pairs
            if (c >= 0xD800 && c <= 0xDBFF)
            {
                // High surrogate
                if (srcPos + 1 < srcLen)
                {
                    char c2 = lpWideCharStr[srcPos + 1];
                    if (c2 >= 0xDC00 && c2 <= 0xDFFF)
                    {
                        // Valid surrogate pair
                        codePoint = 0x10000 + (((c - 0xD800) << 10) | (c2 - 0xDC00));
                        srcPos += 2;
                    }
                    else
                    {
                        // Invalid: high surrogate not followed by low
                        if ((dwFlags & WcFlags.WC_ERR_INVALID_CHARS) != 0)
                            return 0;
                        codePoint = 0xFFFD;
                        srcPos++;
                    }
                }
                else
                {
                    // Incomplete surrogate pair at end
                    if ((dwFlags & WcFlags.WC_ERR_INVALID_CHARS) != 0)
                        return 0;
                    codePoint = 0xFFFD;
                    srcPos++;
                }
            }
            else if (c >= 0xDC00 && c <= 0xDFFF)
            {
                // Unpaired low surrogate
                if ((dwFlags & WcFlags.WC_ERR_INVALID_CHARS) != 0)
                    return 0;
                codePoint = 0xFFFD;
                srcPos++;
            }
            else
            {
                codePoint = c;
                srcPos++;
            }

            // Count UTF-8 bytes needed
            if (codePoint <= 0x7F)
                requiredBytes += 1;
            else if (codePoint <= 0x7FF)
                requiredBytes += 2;
            else if (codePoint <= 0xFFFF)
                requiredBytes += 3;
            else
                requiredBytes += 4;
        }

        // If lpMultiByteStr is null, just return required size
        if (lpMultiByteStr == null || cbMultiByte == 0)
            return requiredBytes;

        // Check if buffer is large enough
        if (cbMultiByte < requiredBytes)
            return 0; // Buffer too small

        // Second pass: actually convert
        srcPos = 0;
        int dstPos = 0;

        while (srcPos < srcLen && dstPos < cbMultiByte)
        {
            char c = lpWideCharStr[srcPos];

            if (cchWideChar == -1 && c == 0)
            {
                lpMultiByteStr[dstPos++] = 0;
                break;
            }

            int codePoint;

            // Handle surrogate pairs
            if (c >= 0xD800 && c <= 0xDBFF && srcPos + 1 < srcLen)
            {
                char c2 = lpWideCharStr[srcPos + 1];
                if (c2 >= 0xDC00 && c2 <= 0xDFFF)
                {
                    codePoint = 0x10000 + (((c - 0xD800) << 10) | (c2 - 0xDC00));
                    srcPos += 2;
                }
                else
                {
                    codePoint = 0xFFFD;
                    srcPos++;
                }
            }
            else if (c >= 0xD800 && c <= 0xDFFF)
            {
                codePoint = 0xFFFD;
                srcPos++;
            }
            else
            {
                codePoint = c;
                srcPos++;
            }

            // Write UTF-8
            if (codePoint <= 0x7F)
            {
                lpMultiByteStr[dstPos++] = (byte)codePoint;
            }
            else if (codePoint <= 0x7FF)
            {
                if (dstPos + 1 >= cbMultiByte)
                    return 0;
                lpMultiByteStr[dstPos++] = (byte)(0xC0 | (codePoint >> 6));
                lpMultiByteStr[dstPos++] = (byte)(0x80 | (codePoint & 0x3F));
            }
            else if (codePoint <= 0xFFFF)
            {
                if (dstPos + 2 >= cbMultiByte)
                    return 0;
                lpMultiByteStr[dstPos++] = (byte)(0xE0 | (codePoint >> 12));
                lpMultiByteStr[dstPos++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                lpMultiByteStr[dstPos++] = (byte)(0x80 | (codePoint & 0x3F));
            }
            else
            {
                if (dstPos + 3 >= cbMultiByte)
                    return 0;
                lpMultiByteStr[dstPos++] = (byte)(0xF0 | (codePoint >> 18));
                lpMultiByteStr[dstPos++] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
                lpMultiByteStr[dstPos++] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
                lpMultiByteStr[dstPos++] = (byte)(0x80 | (codePoint & 0x3F));
            }
        }

        return dstPos;
    }

    /// <summary>
    /// Get the current ANSI code page.
    /// We return UTF-8 (65001) as the default.
    /// </summary>
    public static uint GetACP()
    {
        return CodePage.CP_UTF8;
    }

    /// <summary>
    /// Get information about a code page.
    /// </summary>
    /// <param name="codePage">Code page to query</param>
    /// <param name="lpCPInfo">Pointer to CPINFO structure</param>
    /// <returns>True on success</returns>
    public static bool GetCPInfo(uint codePage, CpInfo* lpCPInfo)
    {
        if (lpCPInfo == null)
            return false;

        // Normalize code page
        if (codePage == CodePage.CP_ACP)
            codePage = CodePage.CP_UTF8;

        switch (codePage)
        {
            case CodePage.CP_UTF8:
                lpCPInfo->MaxCharSize = 4; // UTF-8 can be up to 4 bytes
                lpCPInfo->DefaultChar[0] = (byte)'?';
                lpCPInfo->DefaultChar[1] = 0;
                // No lead bytes for UTF-8
                for (int i = 0; i < 12; i++)
                    lpCPInfo->LeadByte[i] = 0;
                return true;

            default:
                // Unknown code page
                return false;
        }
    }
}
