param(
    [string]$SvgPath = (Join-Path $PSScriptRoot '..\docs\images\orla-mark.svg'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\orla.ico')
)

$ErrorActionPreference = 'Stop'
$edgeCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
)
$edge = $edgeCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $edge) {
    throw 'Microsoft Edge was not found; it is used only to render the SVG while generating the icon.'
}

$svg = (Resolve-Path -LiteralPath $SvgPath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
$temporary = Join-Path $env:TEMP ('orla-icon-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary -Force | Out-Null

$iconBuilder = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class OrlaIconBuilder
{
    public static void Build(string whitePath, string blackPath, string outputPath)
    {
        using (Bitmap white = new Bitmap(whitePath))
        using (Bitmap black = new Bitmap(blackPath))
        using (Bitmap source = RestoreAlpha(white, black))
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes) images.Add(RenderPng(source, size));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            using (BinaryWriter writer = new BinaryWriter(File.Create(outputPath)))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                int offset = 6 + (16 * sizes.Length);
                for (int index = 0; index < sizes.Length; index++)
                {
                    int size = sizes[index];
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)images[index].Length);
                    writer.Write((uint)offset);
                    offset += images[index].Length;
                }
                foreach (byte[] image in images) writer.Write(image);
            }
        }
    }

    private static Bitmap RestoreAlpha(Bitmap whiteSource, Bitmap blackSource)
    {
        int width = whiteSource.Width;
        int height = whiteSource.Height;
        Bitmap white = whiteSource.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppArgb);
        Bitmap black = blackSource.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppArgb);
        Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        Rectangle bounds = new Rectangle(0, 0, width, height);
        BitmapData whiteData = white.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData blackData = black.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData resultData = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        int bytes = Math.Abs(whiteData.Stride) * height;
        byte[] w = new byte[bytes];
        byte[] b = new byte[bytes];
        byte[] r = new byte[bytes];
        Marshal.Copy(whiteData.Scan0, w, 0, bytes);
        Marshal.Copy(blackData.Scan0, b, 0, bytes);
        for (int index = 0; index < bytes; index += 4)
        {
            int background = ((w[index] - b[index]) + (w[index + 1] - b[index + 1]) + (w[index + 2] - b[index + 2])) / 3;
            int alpha = Math.Max(0, Math.Min(255, 255 - background));
            r[index + 3] = (byte)alpha;
            if (alpha == 0) continue;
            r[index] = (byte)Math.Min(255, (b[index] * 255 + alpha / 2) / alpha);
            r[index + 1] = (byte)Math.Min(255, (b[index + 1] * 255 + alpha / 2) / alpha);
            r[index + 2] = (byte)Math.Min(255, (b[index + 2] * 255 + alpha / 2) / alpha);
        }
        Marshal.Copy(r, 0, resultData.Scan0, bytes);
        white.UnlockBits(whiteData);
        black.UnlockBits(blackData);
        result.UnlockBits(resultData);
        white.Dispose();
        black.Dispose();
        return result;
    }

    private static byte[] RenderPng(Bitmap source, int size)
    {
        using (Bitmap image = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(image))
        using (MemoryStream stream = new MemoryStream())
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
'@

try {
    $white = Join-Path $temporary 'white.png'
    $black = Join-Path $temporary 'black.png'
    $uri = ([Uri]$svg).AbsoluteUri
    foreach ($render in @(
        @{ Path = $white; Background = 'ffffffff' },
        @{ Path = $black; Background = 'ff000000' }
    )) {
        $arguments = @(
            '--headless',
            '--disable-gpu',
            '--hide-scrollbars',
            '--window-size=1024,1024',
            ('--default-background-color=' + $render.Background),
            ('--screenshot="' + $render.Path + '"'),
            $uri
        )
        $process = Start-Process -FilePath $edge -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $render.Path)) {
            throw 'Could not render the Orla mark.'
        }
    }

    Add-Type -TypeDefinition $iconBuilder -ReferencedAssemblies System.Drawing
    [OrlaIconBuilder]::Build($white, $black, $output)
    $item = Get-Item -LiteralPath $output
    Write-Output "Icon generated: $($item.FullName) ($($item.Length) bytes)"
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
