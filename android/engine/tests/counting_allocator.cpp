#include "counting_allocator.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdlib>
#include <new>

namespace {

constexpr size_t kWatched = 8;

thread_local int64_t allocations = 0;
thread_local std::array<const void*, kWatched> watched{};
thread_local std::array<int64_t, kWatched> frees{};

void* allocate(std::size_t size, std::size_t alignment) {
    ++allocations;
    void* memory = nullptr;
    if (posix_memalign(&memory, std::max(alignment, sizeof(void*)), size == 0 ? 1 : size) != 0) throw std::bad_alloc();
    return memory;
}

void release(void* memory) noexcept {
    if (memory == nullptr) return;
    for (size_t i = 0; i < kWatched; ++i) {
        if (watched[i] == memory) ++frees[i];
    }
    std::free(memory);
}

}

namespace aria::test {

int64_t allocationsOnThisThread() { return allocations; }

void watchFreesOnThisThread(std::initializer_list<const void*> addresses) {
    watched.fill(nullptr);
    frees.fill(0);
    std::copy_n(addresses.begin(), std::min(addresses.size(), kWatched), watched.begin());
}

int64_t freesOf(const void* address) {
    const auto found = std::find(watched.begin(), watched.end(), address);
    return found == watched.end() ? -1 : frees[static_cast<size_t>(found - watched.begin())];
}

}

void* operator new(std::size_t size) { return allocate(size, __STDCPP_DEFAULT_NEW_ALIGNMENT__); }

void* operator new[](std::size_t size) { return allocate(size, __STDCPP_DEFAULT_NEW_ALIGNMENT__); }

void* operator new(std::size_t size, std::align_val_t alignment) { return allocate(size, static_cast<std::size_t>(alignment)); }

void* operator new[](std::size_t size, std::align_val_t alignment) { return allocate(size, static_cast<std::size_t>(alignment)); }

void operator delete(void* memory) noexcept { release(memory); }

void operator delete[](void* memory) noexcept { release(memory); }

void operator delete(void* memory, std::size_t) noexcept { release(memory); }

void operator delete[](void* memory, std::size_t) noexcept { release(memory); }

void operator delete(void* memory, std::align_val_t) noexcept { release(memory); }

void operator delete[](void* memory, std::align_val_t) noexcept { release(memory); }

void operator delete(void* memory, std::size_t, std::align_val_t) noexcept { release(memory); }

void operator delete[](void* memory, std::size_t, std::align_val_t) noexcept { release(memory); }
