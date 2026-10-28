#pragma once

#include <cstdint>
#include <initializer_list>

namespace aria::test {

int64_t allocationsOnThisThread();
void watchFreesOnThisThread(std::initializer_list<const void*> addresses);
int64_t freesOf(const void* address);

}
