using System;
using CelineBitmap.Models;
using OpenCvSharp;

namespace CelineBitmap.Services;

public static class JangsanVectorExtractor
{
    const int FeatureSize = 64;

    /// <summary>
    /// 강아지 장산이 얼굴 이미지를 64x64 Gray 이미지로 변환한 뒤
    /// 4096차원 특징 벡터를 생성한다
    /// 
    /// RainAI 신경망 임베딩이 아닌
    /// OpenCV 기초 실험용 Vector
    /// </summary>
    public static JangsanVector Extract(Mat face)
    {
        // 빈 데이터 오류 방지
        if (face.Empty())
        {
            throw new ArgumentException("강아지 얼굴 이미지가 비어 있습니다.", nameof(face));
        }

        // Grayscale: 채널 수에 따른 흑백 변환
        // 얼굴 인식이나 특징 추출에는 불필요한 연산량을 줄이기 위해 흑백화
        using var gray = new Mat();

        switch (face.Channels())
        {
            case 4: // 투명도 포함
                Cv2.CvtColor(face, gray, ColorConversionCodes.BGRA2GRAY);
                break;

            case 3: // 일반 색상
                Cv2.CvtColor(face, gray, ColorConversionCodes.BGR2GRAY);
                break;

            case 1: // 이미 흑백
                face.CopyTo(gray);
                break;

            default:
                throw new NotSupportedException($"지원하지 않는 채널 수: {face.Channels()}");
        }

        // 히스토그램 평활화(Histogram Equalization)
        // 조명 차이를 어느 정도 줄인다
        // 사진이 어둡거나 밝을 때 조명 차이를 보정하여 동일한 환경 조건으로 만들기
        using var equalized = new Mat();
        Cv2.EqualizeHist(gray, equalized);

        // 모든 얼굴을 64 x 64로 통일
        using var resized = new Mat();
        Cv2.Resize(equalized, resized, new Size(FeatureSize, FeatureSize), 0, 0, InterpolationFlags.Area);

        // byte -> float
        // 0~255 -> 0.0~1.0
        using var floatImage = new Mat();
        resized.ConvertTo(floatImage, MatType.CV_32F, 1.0 / 255.0);

        // 2D 이미지 -> 1차원 Vector
        var rows = floatImage.AsRows<float>();
        float[] vector = new float[FeatureSize * FeatureSize];

        int index = 0;

        for (int y = 0; y < FeatureSize; y++)
        {
            Span<float> row = rows[y];

            for (int x = 0; x < FeatureSize; x++)
            {
                vector[index] = row[x];
                index++;
            }
        }

        // 벡터 크기를 1로 정규화
        NormalizeL2(vector);

        // 장산이 데이터 반환
        return new JangsanVector
        {
            Name = "Jangsan",
            CapturedAt = DateTimeOffset.Now,
            FeatureWidth = FeatureSize,
            FeatureHeight = FeatureSize,
            Method = "Gray64x64-L2",
            Values = vector
        };
    }

    /// <summary>
    /// L2 정규화
    /// Vector / Vector Length
    /// </summary>
    static void NormalizeL2(float[] vector)
    {
        // 각 원소들의 제곱 값을 누적하여 정밀도를 위해 double 자료형
        double sum = 0;

        // 피타고라스 정리로 Vector의 총 길이 계산
        foreach (float value in vector)
        {
            sum += value * value;
        }

        double length = Math.Sqrt(sum);

        // 혹시 모든 픽셀 값이 완전히 0이라면
        // DivideByZero 오류 발생 방지
        if (length < 0.000001) return;

        // 모든 원소를 총 길이로 나누어 길이 1로 만들기
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / length);
        }
    }
}