extends SceneTree

# Regression for the player-owned zoom contract. The earlier obstruction ray was
# author-rejected because walking beside a pillar pulled the camera close and then
# made it drift outward. A solid body on the sight line must now leave the chosen
# camera distance and rendered transform unchanged.

func _initialize() -> void:
	call_deferred("_run")

func _box_body(position: Vector3, size: Vector3) -> StaticBody3D:
	var body := StaticBody3D.new()
	body.name = "BlockingObject"
	body.position = position
	var shape := BoxShape3D.new()
	shape.size = size
	var collision := CollisionShape3D.new()
	collision.shape = shape
	body.add_child(collision)
	root.add_child(body)
	return body

func _assert_fixed_distance(rig, focus: Vector3, stage: String) -> bool:
	if abs(rig.Distance - 75.0) > 0.001 or abs(rig.TargetDistance - 75.0) > 0.001:
		push_error("%s changed selected camera zoom: %s / %s" %
			[stage, rig.Distance, rig.TargetDistance])
		return false
	var rendered: float = rig.global_position.distance_to(focus)
	if abs(rendered - 75.0) > 0.01:
		push_error("%s changed rendered camera distance: %s" % [stage, rendered])
		return false
	return true

func _run() -> void:
	var rig_script = load("res://src/Render/CameraRig.cs")
	var rig = rig_script.new()
	root.add_child(rig)
	await process_frame

	rig.Distance = 75.0
	rig.TargetDistance = 75.0
	var focus := Vector3(0.0, 1.6, 0.0)
	for frame in range(20):
		rig.Follow(Vector3.ZERO, Vector3.ZERO, 0.1)
	var direction := Vector3(
		sin(rig.Yaw) * cos(rig.Pitch),
		sin(rig.Pitch),
		cos(rig.Yaw) * cos(rig.Pitch)).normalized()

	var blocker := _box_body(focus + direction * 25.0,
		Vector3(7.0, 12.0, 7.0))
	await physics_frame
	await physics_frame
	for frame in range(10):
		rig.Follow(Vector3.ZERO, Vector3.ZERO, 0.1)
	if not _assert_fixed_distance(rig, focus, "blocking object"):
		quit(1)
		return

	blocker.queue_free()
	await physics_frame
	await physics_frame
	for frame in range(10):
		rig.Follow(Vector3.ZERO, Vector3.ZERO, 0.1)
	if not _assert_fixed_distance(rig, focus, "walking away"):
		quit(1)
		return

	print("[camera-obstruction-smoke] blocking object and removal leave selected and rendered distance fixed at 75")
	quit()
