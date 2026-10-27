#pragma once

#include "plan.h"

namespace aria {

double curve(const char* name, double t);
double fadeLevel(const FadePlan* fade, double framesSinceAnchor);
double dbToGain(double db);

}
