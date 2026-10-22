// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203: every area's engine-free checks (one file per area, wo203_<area>_tests.cpp). Linked into KCDMP_NativeTests.
int wo203_gate_tests(int* passed);   // I.5: the node gate's scene rules (wo203_gate_tests.cpp)
int wo203_conv_tests(int* passed);
int wo203_defence_tests(int* passed);
int wo203_stream_tests(int* passed);
int wo203_appear_tests(int* passed);
int wo203_quest_tests(int* passed);
int wo203_join_tests(int* passed);
int wo203_cam_tests(int* passed);    // WO-203B: the conversation camera (wo203_cam_tests.cpp)

int wo203_tests(int* passed) {
    int (*const areas[])(int*) = {&wo203_gate_tests, &wo203_conv_tests, &wo203_defence_tests, &wo203_stream_tests, &wo203_appear_tests,
                                  &wo203_quest_tests, &wo203_join_tests, &wo203_cam_tests};
    int fail = 0, pass = 0;
    for (auto fn : areas) {
        int p = 0;
        fail += fn(&p);
        pass += p;
    }
    *passed = pass;
    return fail;
}
