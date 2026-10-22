"""Add reusable Seeker attachment pivots and export the Unity FBX."""

import bpy
from mathutils import Vector

BLEND_PATH = r"C:\CS\Y3S1\GV\Game\OVERIDE\ArtSource\Seeker\Seeker_Hunter.blend"
FBX_PATH = r"C:\CS\Y3S1\GV\Game\OVERIDE\Assets\_Project\Art\Models\Seeker\Seeker_Hunter.fbx"


def world_bounds_center(object_name):
    obj = bpy.data.objects.get(object_name)
    if obj is None or obj.type != "MESH":
        raise RuntimeError(f"Required Seeker mesh is missing: {object_name}")
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = Vector(tuple(min(p[i] for p in corners) for i in range(3)))
    maximum = Vector(tuple(max(p[i] for p in corners) for i in range(3)))
    return (minimum + maximum) * 0.5


def ensure_empty(name, location, parent=None):
    marker = bpy.data.objects.get(name)
    if marker is not None and marker.type != "EMPTY":
        raise RuntimeError(f"Seeker pivot name is already used by a non-empty object: {name}")
    if marker is None:
        marker = bpy.data.objects.new(name, None)
        bpy.context.scene.collection.objects.link(marker)
    marker.empty_display_type = "PLAIN_AXES"
    marker.empty_display_size = 0.12
    marker.location = location
    marker.rotation_euler = (0.0, 0.0, 0.0)
    marker.scale = (1.0, 1.0, 1.0)
    marker.parent = parent
    return marker


def parent_keep_world(child, parent):
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()
    child.matrix_world = world
    bpy.context.view_layer.update()


required_meshes = {
    "Seeker_Antennae", "Seeker_ArmouredBody", "Seeker_Crawler_Left",
    "Seeker_Crawler_Right", "Seeker_FrontSensorHousing", "Seeker_RearThruster",
    "Seeker_StatusLEDs", "Seeker_Thruster_Left", "Seeker_Thruster_Right",
    "SensorCore_Catchlight", "SensorCore_OuterHalo", "SensorCore_Pupil",
    "SensorCore_WeakPoint",
}
missing = sorted(name for name in required_meshes if bpy.data.objects.get(name) is None)
if missing:
    raise RuntimeError("Missing required Seeker meshes: " + ", ".join(missing))

legacy_root = bpy.data.objects.get("Seeker_Hunter_Root")
root = bpy.data.objects.get("SeekerRoot")
if root is None and legacy_root is not None:
    legacy_root.name = "SeekerRoot"
root = ensure_empty("SeekerRoot", Vector((0.0, 0.0, 0.0)))
sensor_core = ensure_empty("SensorCore", world_bounds_center("SensorCore_WeakPoint"), root)
sensor_housing = ensure_empty("SensorHousing", world_bounds_center("Seeker_FrontSensorHousing"), root)
left_thruster = ensure_empty("LeftThruster", world_bounds_center("Seeker_Thruster_Left"), root)
right_thruster = ensure_empty("RightThruster", world_bounds_center("Seeker_Thruster_Right"), root)
rear_thruster = ensure_empty("RearThruster", world_bounds_center("Seeker_RearThruster"), root)
bpy.context.view_layer.update()

for name in ("SensorCore_Catchlight", "SensorCore_OuterHalo", "SensorCore_Pupil", "SensorCore_WeakPoint"):
    parent_keep_world(bpy.data.objects[name], sensor_core)
parent_keep_world(bpy.data.objects["Seeker_FrontSensorHousing"], sensor_housing)
parent_keep_world(bpy.data.objects["Seeker_Thruster_Left"], left_thruster)
parent_keep_world(bpy.data.objects["Seeker_Thruster_Right"], right_thruster)
parent_keep_world(bpy.data.objects["Seeker_RearThruster"], rear_thruster)

for name in sorted(required_meshes):
    obj = bpy.data.objects[name]
    if obj.parent not in {sensor_core, sensor_housing, left_thruster, right_thruster, rear_thruster}:
        parent_keep_world(obj, root)

for legacy_name in ("SeekerSensorEye", "SeekerWeakPoint", "SeekerCenter", "SeekerRearExhaust"):
    legacy_marker = bpy.data.objects.get(legacy_name)
    if legacy_marker is not None:
        bpy.data.objects.remove(legacy_marker, do_unlink=True)

for old_root_name in ("world", "Seeker_Hunter_Root"):
    old_root = bpy.data.objects.get(old_root_name)
    if old_root is not None and old_root != root and not old_root.children:
        bpy.data.objects.remove(old_root, do_unlink=True)

bpy.ops.object.select_all(action="DESELECT")
root.select_set(True)
for child in root.children_recursive:
    child.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
bpy.ops.export_scene.fbx(
    filepath=FBX_PATH, use_selection=True, object_types={"MESH", "EMPTY"},
    axis_forward="-Z", axis_up="Y", bake_anim=False, add_leaf_bones=False,
    path_mode="AUTO",
)
print("Seeker source and FBX regenerated with functional attachment pivots.")
