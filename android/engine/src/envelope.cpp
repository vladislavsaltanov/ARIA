#include "envelope.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace aria {

double curve(const char* name, double t) {
    if (std::strcmp(name, "Logarithmic") == 0) return std::sqrt(t);
    if (std::strcmp(name, "Exponential") == 0) return t * t;
    if (std::strcmp(name, "SCurve") == 0) return t * t * (3.0 - 2.0 * t);
    return t;
}

double fadeLevel(const FadePlan* fade, double framesSinceAnchor) {
    if (fade == nullptr) return 1.0;
    const double t = std::min(1.0, std::max(0.0, fade->progress + framesSinceAnchor / static_cast<double>(std::max(1, fade->frames))));
    return fade->from + (fade->to - fade->from) * curve(fade->curve.c_str(), t);
}

double dbToGain(double db) { return std::pow(10.0, db / 20.0); }

}
