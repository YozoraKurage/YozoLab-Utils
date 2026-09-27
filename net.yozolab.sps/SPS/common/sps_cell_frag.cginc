#ifndef SPS_INC_CELL_FRAG
#define SPS_INC_CELL_FRAG

#include "sps_dictionary.cginc"
#include "sps_utils.cginc"

inline bool sps_cell_frag(
    int cellIndex,
    float4 vertex,
    out uint pixelIndex,
    out float4 rgba
) {
    pixelIndex = 0u;
    rgba = 0;
    if (sps_should_abort()) {
        clip(-1);
        return true;
    }

    // Cells are laid out bottom-up. Direct3D's SV_Position has its origin at the top-left, so it
    // is flipped here; OpenGL and Vulkan (as translated by Unity) already count from the bottom.
    // (YozoLab SPS: added so the editor preview also works outside Direct3D.)
    int2 local = int2(
        floor(vertex.x),
        #if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_VULKAN)
        floor(vertex.y)
        #else
        floor(_ScreenParams.y - vertex.y)
        #endif
    ) - sps_cell_origin_from_index(cellIndex);
    sps_clip_rect(local, int2(SPS_CELL_WIDTH, SPS_CELL_HEIGHT));
    pixelIndex = (uint)local.x + (uint)local.y * (uint)SPS_CELL_WIDTH;

    if (sps_dictionary_frag(cellIndex, pixelIndex, rgba)) {
        return true;
    }

    return false;
}

#endif
