#ifndef FARM_WIND_INCLUDED
#define FARM_WIND_INCLUDED

// Глобальный ветер на всю ферму. Выставляется из WindController.
// _FarmWind: xy — направление в плоскости земли, z — сила, w — скорость.
// _FarmWindGust: x — текущий порыв (множитель силы), y — множитель дрожи.
float4 _FarmWind;
float4 _FarmWindGust;

float FarmWindHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

/// Смещение вершины от ветра, в мировом пространстве.
///
/// positionOS — вершина в пространстве объекта: высота считается от основания растения,
/// иначе трава на пригорке качалась бы сильнее травы в низине.
/// originWS — начало координат объекта: даёт каждому кусту свою фазу, чтобы соседи
/// не качались строем.
float3 FarmWindOffset(float3 positionOS, float3 positionWS, float3 originWS,
                      float strength, float speed, float turbulence,
                      float maskHeight, float phaseScale)
{
    // Основание прибито к земле, верхушка гуляет. Квадрат — чтобы изгиб шёл по дуге,
    // а не ломался у корня.
    float mask = saturate(max(positionOS.y, 0.0) / max(maskHeight, 0.001));
    mask *= mask;

    float2 dir = _FarmWind.xy;
    float dirLength = max(length(dir), 1e-4);
    dir /= dirLength;

    float force = _FarmWind.z * _FarmWindGust.x * strength;
    if (force <= 0.0) return float3(0.0, 0.0, 0.0);

    float phase = FarmWindHash(originWS.xz) * 6.2831853 * phaseScale;

    // Волна бежит по ветру: растения дальше по направлению вступают с задержкой,
    // и порыв читается как порыв, а не как одновременное вздрагивание всего поля.
    float travel = dot(positionWS.xz, dir) * 0.35;

    float t = _Time.y * _FarmWind.w * speed + phase - travel;

    // Две синусоиды с несоизмеримыми периодами. Одна читается как метроном.
    float bend = sin(t) * 0.75 + sin(t * 2.37 + 1.13) * 0.25;

    // Мелкая дрожь отдельных листьев поверх общего изгиба.
    float flutter = sin(t * 5.3 + (positionWS.x + positionWS.z) * 3.1)
                  * turbulence * _FarmWindGust.y;

    float amount = (bend + flutter) * force * mask;

    float3 offset;
    offset.xz = dir * amount;

    // Верхушку слегка подтягиваем вниз: стебель гнётся, а не растягивается вбок.
    offset.y = -abs(amount) * 0.25 * mask;
    return offset;
}

#endif
