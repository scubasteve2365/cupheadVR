cmake_minimum_required(VERSION 3.20)
project(CupheadVRNative CXX)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")
set(OPENXR_DIR "C:/VRLab/bin/openxr_loader_1.1.65" CACHE PATH "Khronos OpenXR loader folder")
add_library(CupheadVRNative SHARED CupheadVRNative.cpp)
target_include_directories(CupheadVRNative PRIVATE ${OPENXR_DIR}/include)
target_link_libraries(CupheadVRNative PRIVATE d3d11 dxgi advapi32)
set_target_properties(CupheadVRNative PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/../../build")
add_custom_command(TARGET CupheadVRNative POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${OPENXR_DIR}/x64/bin/openxr_loader.dll" "${CMAKE_SOURCE_DIR}/../../build/openxr_loader.dll")
