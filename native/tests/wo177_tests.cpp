// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-177: engine-free checks of the native log rotation (log.h).
// Linked into KCDMP_NativeTests; wo177_tests() returns the number of failures.
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>

#include "log.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

void put(const std::filesystem::path& p, const std::string& body) { std::ofstream(p, std::ios::binary) << body; }
std::string get(const std::filesystem::path& p) {
    std::ifstream f(p, std::ios::binary);
    return std::string(std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>());
}
} // namespace

int wo177_tests(int* passed) {
    // ---- A4: the numbered names
    WCHECK(numbered_log_name("C:\\a\\kcdmp-native.log", 1) == "C:\\a\\kcdmp-native.1.log", "primary: kcdmp-native.log -> kcdmp-native.1.log");
    WCHECK(numbered_log_name("D:\\g\\kcdmp-native.mirror.log", 5) == "D:\\g\\kcdmp-native.mirror.5.log", "mirror: .mirror.log -> .mirror.5.log");
    WCHECK(numbered_log_name("C:\\dir.d\\noext", 2) == "C:\\dir.d\\noext.2", "a dot in the folder name is not an extension");
    WCHECK(kLogKeep == 5, "the last five logs are kept");

    // ---- A4: the rotation, on real files
    namespace fs = std::filesystem;
    const fs::path dir = fs::temp_directory_path() / "kcdmp-wo177-logtest";
    std::error_code ec;
    fs::remove_all(dir, ec);
    fs::create_directories(dir, ec);
    const std::string base = (dir / "kcdmp-native.log").string();
    WCHECK(rotate_log_files(base) == 0, "no earlier log -> nothing to keep, nothing created");
    WCHECK(!fs::exists(dir / "kcdmp-native.1.log"), "no earlier log -> no .1 file");
    for (int run = 1; run <= 7; ++run) {
        put(base, "run" + std::to_string(run));   // what the previous injection left
        const int kept = rotate_log_files(base);
        WCHECK(kept == (run < 5 ? run : 5), "run %d: %d earlier logs kept (got %d)", run, run < 5 ? run : 5, kept);
        WCHECK(!fs::exists(base), "run %d: the current log was moved aside, not truncated in place", run);
    }
    // after 7 runs: .1 = newest earlier (run7), .5 = oldest kept (run3); runs 1 and 2 are gone
    WCHECK(get(dir / "kcdmp-native.1.log") == "run7", ".1 holds the newest earlier log");
    WCHECK(get(dir / "kcdmp-native.2.log") == "run6" && get(dir / "kcdmp-native.3.log") == "run5" && get(dir / "kcdmp-native.4.log") == "run4", ".2 .3 .4 shift down one each time");
    WCHECK(get(dir / "kcdmp-native.5.log") == "run3", ".5 holds the oldest kept log");
    WCHECK(!fs::exists(dir / "kcdmp-native.6.log"), "never a sixth");
    // the mirror's own names never collide with the primary's
    put((dir / "kcdmp-native.mirror.log").string(), "mirror1");
    WCHECK(rotate_log_files((dir / "kcdmp-native.mirror.log").string()) == 1 && get(dir / "kcdmp-native.mirror.1.log") == "mirror1" && get(dir / "kcdmp-native.1.log") == "run7",
           "the mirror rotates under its own numbers, the primary's files are untouched");
    fs::remove_all(dir, ec);

    if (passed) *passed = g_pass;
    return g_fail;
}
