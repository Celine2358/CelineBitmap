using System;

namespace CelineBitmap.Models;

/// <summary>
/// 강아지 장산이 얼굴 이미지에서 추출한
/// 실험용 특징 벡터 데이터!
/// </summary>
public sealed class JangsanVector
{
    // 데이터 이름
    public string Name { get; init; } = "Jangsan";

    // 촬영 시간
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    // 특징 추출에 사용한 이미지 크기
    public int FeatureWidth { get; init; }
    public int FeatureHeight { get; init; }

    // 어떤 알고리즘으로 만든 Vector인지 기록하기
    public string Method { get; init; } = "Gray64x64-L2";

    // 실제 특징값
    public float[] Values { get; init; } = [];
}