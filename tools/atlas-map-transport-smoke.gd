extends SceneTree

# Bootstrap the C# smoke so the check can load the typed atlas definition and
# exercise the same AtlasWorldMap instance used by production.

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var smoke_script = load("res://tools/AtlasMapTransportSmoke.cs")
	var smoke = smoke_script.new()
	root.add_child(smoke)
