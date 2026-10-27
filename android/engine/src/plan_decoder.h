#pragma once

#include "plan.h"

#include <cstddef>
#include <cstdint>
#include <memory>

namespace aria {

std::unique_ptr<RenderPlan> decodePlan(const uint8_t* data, size_t size);

}
