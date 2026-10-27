#pragma once

namespace aria {

inline float cubic4(const float* p, double frac) {
    const float x = static_cast<float>(frac);
    const float slope = 0.5F * (p[2] - p[0]);
    const float bend = p[0] - 2.5F * p[1] + 2.0F * p[2] - 0.5F * p[3];
    const float twist = 0.5F * (p[3] - p[0]) + 1.5F * (p[1] - p[2]);
    return ((twist * x + bend) * x + slope) * x + p[1];
}

}
