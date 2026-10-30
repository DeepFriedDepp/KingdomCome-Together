# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
add_library(KCDMP SHARED
    dllmain.cpp
    rttr_probe.cpp
    rttr_abi.cpp
    combat_playanim.cpp
    combat_construct.cpp
    script_context.cpp
    main_thread.cpp
    pipe_server.cpp
    dice_hook.cpp
    lua_closure.cpp
    concept_read.cpp
    mannequin_read.cpp
    combat_write.cpp
    local_state.cpp
    npc_scan.cpp
    anchors.cpp
    engine.cpp
    buffs.cpp
    gameover_hook.cpp
    hangover.cpp
    respawn.cpp
    respawn_actions.cpp
    punishment.cpp
    npc_drive.cpp
    npc_trace.cpp
    inline_hook.cpp
    motion.cpp
    hits.cpp
    savelist.cpp
    join_native.cpp
    leash.cpp
    wo131.cpp
    wo132.cpp
    wo135.cpp
    wo136.cpp
    wo137.cpp
    wo138.cpp
    wo139.cpp
    wo140.cpp
    wo141.cpp
    wo143.cpp
    wo147.cpp
    fault_guard.cpp
    wo151.cpp
    wo163.cpp
    wo165.cpp
    wo166.cpp
    wo167.cpp
    wo172.cpp
    wo173.cpp
    wo176.cpp
    wo178.cpp
    wo179.cpp
    wo187.cpp
    wo189.cpp
    wo190.cpp
    wo194.cpp
    wo195.cpp
    wo197.cpp
    wo200.cpp
    wo201.cpp
    wo201_near.cpp
    wo201_speech.cpp
    wo201_vote.cpp
    wo201_ranged.cpp
    weather.cpp
    crash_capture.cpp
    wo202.cpp
    wo203.cpp
    wo203_conv.cpp
    wo203_cam.cpp
    wo204.cpp
    wo206.cpp
    wo203_defence.cpp
    wo203_stream.cpp
    wo203_appear.cpp
    wo203_quest.cpp
    wo203_join.cpp
    wo208.cpp
    wo209.cpp
    wo210.cpp
    wo210_kd.cpp
    wo210_hit.cpp
    wo211.cpp
)

target_include_directories(KCDMP PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
set_target_properties(KCDMP PROPERTIES OUTPUT_NAME "KCDMP")
# WO-211 0.2: the build id (tools/Write-BuildId.ps1 writes the repo root's git-ignored BUILD_ID: the short commit and the build time);
# a change re-runs the configure step. No file: "dev".
set(KCDMP_BUILD_ID_FILE "${CMAKE_CURRENT_SOURCE_DIR}/../../BUILD_ID")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${KCDMP_BUILD_ID_FILE}")
set(KCDMP_BUILD_ID "dev")
if(EXISTS "${KCDMP_BUILD_ID_FILE}")
    file(STRINGS "${KCDMP_BUILD_ID_FILE}" _kcdmp_bid LIMIT_COUNT 1 REGEX "^[0-9A-Za-z.-]+$")
    if(_kcdmp_bid)
        set(KCDMP_BUILD_ID "${_kcdmp_bid}")
    endif()
endif()
target_compile_definitions(KCDMP PRIVATE KCDMP_BUILD_ID="${KCDMP_BUILD_ID}")
