shader_type sky;
render_mode disable_fog;
#include "res://Shaders/forest_canopy_radiance.gdshaderinc"
void sky() {
    COLOR = AT_CUBEMAP_PASS ? forest_canopy_radiance(EYEDIR) : enclosure_backdrop(EYEDIR);
}
