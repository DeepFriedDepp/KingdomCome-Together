# WO-129: engine-free native unit tests (no game). Built by Build-Native.ps1,
# run by tools\Build-Installer.ps1 after the native build.
add_executable(KCDMP_NativeTests wo129_gait_tests.cpp wo114_wake_tests.cpp wo133_port_gate_tests.cpp wo136_rules_tests.cpp wo137_rules_tests.cpp wo138_rules_tests.cpp wo139_rules_tests.cpp wo140_rules_tests.cpp wo141_rules_tests.cpp)
target_include_directories(KCDMP_NativeTests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../KCDMP)
