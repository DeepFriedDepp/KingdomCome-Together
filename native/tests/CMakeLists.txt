# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
# WO-129: engine-free native unit tests (no game). Built by Build-Native.ps1,
# run by tools\Build-Installer.ps1 after the native build.
add_executable(KCDMP_NativeTests wo129_gait_tests.cpp wo114_wake_tests.cpp wo133_port_gate_tests.cpp wo136_rules_tests.cpp wo137_rules_tests.cpp wo138_rules_tests.cpp wo139_rules_tests.cpp wo140_rules_tests.cpp wo141_rules_tests.cpp wo143_rules_tests.cpp wo148_x64_tests.cpp wo151_tests.cpp wo155_rules_tests.cpp wo163_tests.cpp wo165_tests.cpp wo166_tests.cpp wo167_tests.cpp wo170_tests.cpp wo172_tests.cpp wo171_tests.cpp wo173_tests.cpp wo174_tests.cpp wo176_tests.cpp wo177_tests.cpp wo178_tests.cpp wo187_tests.cpp wo190_tests.cpp wo194_tests.cpp wo195_tests.cpp wo197_tests.cpp wo200_tests.cpp ../KCDMP/fault_guard.cpp)
target_include_directories(KCDMP_NativeTests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../KCDMP)
