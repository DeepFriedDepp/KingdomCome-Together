# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
# WO-72 experiment target. Not part of the shipped product; nothing installs
# this DLL. Built alongside the real targets only so it uses the same toolchain
# and runtime as the plugin it borrows conventions from.
add_library(WO72NullRenderer SHARED main.cpp)
target_link_libraries(WO72NullRenderer PRIVATE psapi)
set_target_properties(WO72NullRenderer PROPERTIES OUTPUT_NAME "WO72NullRenderer")
