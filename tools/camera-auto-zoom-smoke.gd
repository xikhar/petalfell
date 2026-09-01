extends SceneTree


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var rig_script = load("res://src/Render/CameraRig.cs")
	var rig = rig_script.new()
	root.add_child(rig)
	await process_frame

	rig.Distance = 75.0
	rig.TargetDistance = 75.0
	rig.MaxDistance = 120.0
	rig.AutoZoomSpeed = 12.0
	rig.StartAutoZoomToMaximum()
	for frame in range(10):
		rig.Follow(Vector3.ZERO, Vector3.ZERO, 0.1)
	if abs(rig.Distance - 87.0) > 0.001 or not rig.AutoZooming:
		push_error("linear auto-zoom failed at one second: %s" % rig.Distance)
		quit(1)
		return

	for frame in range(100):
		rig.Follow(Vector3.ZERO, Vector3.ZERO, 0.1)
	if abs(rig.Distance - 120.0) > 0.001 or rig.AutoZooming:
		push_error("auto-zoom did not stop exactly at maximum: %s" % rig.Distance)
		quit(1)
		return

	rig.Distance = 90.0
	rig.TargetDistance = 90.0
	rig.StartAutoZoomToMaximum()
	rig.Zoom(-6.0)
	if rig.AutoZooming or abs(rig.TargetDistance - 84.0) > 0.001:
		push_error("manual wheel zoom did not cancel auto-zoom")
		quit(1)
		return

	var menu_script = load("res://src/Tools/DeveloperMenu.cs")
	var menu = menu_script.new()
	menu.Setup(null, null, rig, null)
	root.add_child(menu)
	await process_frame
	var speed_slider = _find_speed_slider(menu)
	if speed_slider == null:
		push_error("developer menu has no K auto-zoom speed slider")
		quit(1)
		return
	speed_slider.value = 24.5
	if abs(rig.AutoZoomSpeed - 24.5) > 0.001:
		push_error("developer speed slider did not update the camera")
		quit(1)
		return

	print("[camera-auto-zoom-smoke] linear 75->87 in 1s; exact max 120; wheel cancel target 84; developer speed 24.5")
	quit()


func _find_speed_slider(node: Node):
	if node is Label and node.text == "K auto-zoom speed":
		var group = node.get_parent().get_parent()
		for sibling in group.get_children():
			if sibling is HSlider:
				return sibling
	for child in node.get_children():
		var found = _find_speed_slider(child)
		if found != null:
			return found
	return null
