using System;
using System.IO;
using Avalonia.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.AvaloniaExtensions;

namespace CelineBitmap.Services;

// Mat: 이미지/행렬 데이터
// Cv2: OpenCV 함수들의 중심 클래스
// Size: Width × Height
public static class ImageProcessor
{
    /// <summary>
    /// PNG, JPG 등의 이미지를 OpenCV Mat으로 불러오기
    /// </summary>
    public static Mat Load(string path)
    {
        // Unchanged: PNG의 Alpha 채널 등이 존재하면 가능한 그대로 유지
        Mat image = Cv2.ImRead(path, ImreadModes.Unchanged);

        // 파일을 읽지 못했다면 빈 Mat이 반환될 수 있다!!
        if (image.Empty())
        {
            image.Dispose();
            throw new IOException($"이미지를 불러올 수 없습니다: {path}");
        }

        return image;
    }

    /// <summary>
    /// 이미지의 크기를 변경한다
    /// </summary>
    public static Mat Resize(Mat source, int width, int height, bool pixelArt = false)
    {
        if (source.Empty())
        {
            // 이미지 행렬 데이터 인자가 잘못되었을 때
            throw new ArgumentException("원본 이미지가 비어 있습니다.", nameof(source));
        }

        if (width <= 0 || height <= 0)
        {
            // width와 height 인자의 범위가 잘못되었을 때
            throw new ArgumentOutOfRangeException(nameof(width), "Width와 Height는 1 이상이어야 합니다.");
        }

        var result = new Mat();

        // Pixel Art: 가장 가까운 픽셀을 그대로 복제한다
        // -> 픽셀 경계를 흐리지 않는다
        // 
        // 일반 이미지 확대: Lanczos4를 사용해 주변 픽셀을 계산한다
        // 일반 이미지 축소: Area가 축소에 비교적 적합할 듯

        // 변수: 어떤 보간법을 사용할까?
        InterpolationFlags interpolation;

        if (pixelArt)
        {
            // 인접
            interpolation = InterpolationFlags.Nearest;
        }
        else if (width < source.Width || height < source.Height)
        {
            interpolation = InterpolationFlags.Area;
        }
        else
        {
            interpolation = InterpolationFlags.Lanczos4;
        }

        Cv2.Resize(source, result, new Size(width, height), 0, 0, interpolation);

        return result;
    }

    // OpenCV Mat을 Avalonia UI Image에서 사용할 수 있는 WriteableBitmap으로 변환
    public static WriteableBitmap ToBitmap(Mat image)
    {
        return image.ToWriteableBitmap();
    }

    // Mat을 PNG/JPG 등의 이미지 파일로 저장한다.
    public static void Save(Mat image, string path)
    {
        bool success = Cv2.ImWrite(path, image);

        if (!success)
        {
            // 파일 경로에 문제가 생겼을 때
            throw new IOException($"이미지를 저장하지 못했습니다: {path}");
        }
    }
}