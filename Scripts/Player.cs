using Godot;
using System;

public partial class Player : CharacterBody3D
{
	private class GrapplePoint
	{
		public Vector3 position;
		public GrapplePoint lastGrapplePoint;
		public Vector3 grappleNormal; // Used to determine whether to go back to the last grapple point or not
	}
	[Export] float groundSpeed = 5.0f;
	[Export] float acceleration = 5.0f; // How fast the player accelerates to max speed
	[Export] float drag = 5.0f; // How fast the player decelerates to a stop
	[Export] float jumpVelocity = 4.5f;
	[Export] float airAcceleration = 5.0f; // How fast the player accelerates to max speed while in the air
	[Export] float airDrag = 5.0f; // How fast the player decelerates to a stop while in the air
	[Export] float grapplePullSpeed = 1.0f; // Speed at which the player is pulled towards the grappling hook
	[Export] float grapplePullAcceleration = 5.0f; // Acceleration applied when pulling with the grappling hook
	[Export] float grapplePullBreakDistance = 1.0f; // Distance at which the grapple pull breaks
	[Export] float grappleAscendSpeed = 0.1f; // Speed at which the player ascends while grappling
	[Export] float maxGrappleExtension = 30.0f; // Maximum distance the grappling hook can extend
	[Export] float cameraSensitivity = 0.01f;
	[Export] float terraformSpeed = 0.1f;
	[Export] int terraformRadius = 1;
	[Export] Node3D camRotator;
	[Export] Node3D grappleOrigin; // Origin point for grappling hook
	[Export] RayCast3D interactionRay;
	[Export] RayCast3D hookRay; // Ray for grappling hook interaction
	[Export] TerrainGeneration terrainGeneration;
	[Export] MeshInstance3D grapplingHookMesh; // Mesh for the grappling hook
	bool isGrappleSwinging = false;
	bool isGrapplePulling = false; // Flag to indicate if the player is currently pulling with the grappling hook
	bool isGrappleAscending = false; // Flag to indicate if the player is currently ascending with the grappling hook
	bool isGrappleDescending = false; // Flag to indicate if the player is currently descending with the grappling hook
	GrapplePoint grapplePoint = new GrapplePoint();
	float grappleSwingLength;
	float airSpeed; // Max speed while in the air
	public override void _Ready()
	{
		airSpeed = groundSpeed; // Initialize air speed to ground speed
		grapplingHookMesh.Visible = false; // Hide the grappling hook mesh initially
		Input.MouseMode = Input.MouseModeEnum.Captured;
		terrainGeneration.GenerateFrom(GlobalPosition);
		ProcessMode = ProcessModeEnum.Disabled; // Disable process mode to avoid unnecessary processing
	}
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseEvent)
		{
			// Rotate the player based on the mouse movement.
			RotateObjectLocal(Vector3.Down, mouseEvent.Relative.X * cameraSensitivity);

			camRotator.Rotation = new Vector3(Mathf.Clamp(camRotator.Rotation.X - mouseEvent.Relative.Y * cameraSensitivity, -Mathf.Pi / 2, Mathf.Pi / 2), 0, 0);
			Transform = Transform.Orthonormalized();
			camRotator.Transform = camRotator.Transform.Orthonormalized();

		}
	}
	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;

		if (isGrapplePulling)
		{
			velocity = HandleGrapplePull(velocity, delta);
			Velocity = velocity;
			MoveAndSlide();
			HandleCollisions();
			return; // Skip the rest of the movement logic while grappling
		}

		if (IsOnFloor())
		{
			velocity = HandleGroundedMovement(velocity, delta);
		}
		else
		{
			velocity = HandleAerialMovement(velocity, delta);
		}
		if (isGrappleSwinging)
		{
			velocity = HandleGrappleSwing(velocity, delta);
		}

		Velocity = velocity;
		MoveAndSlide();
		HandleCollisions();
	}

	Vector3? Raycast(Vector3 from, Vector3 to)
	{
		PhysicsRayQueryParameters3D rayParameters = new PhysicsRayQueryParameters3D
		{
			From = from,
			To = to,
			Exclude = new Godot.Collections.Array<Rid> { GetRid() } // Exclude the player from the query
		};
		rayParameters.CollideWithAreas = false;
		rayParameters.CollideWithBodies = true;
		var spaceState = GetWorld3D().DirectSpaceState;
		var result = spaceState.IntersectRay(rayParameters);
		if (result.Count > 0)
		{
			return result["position"].AsVector3(); // Convert Variant to Vector3
		}
		return null; // Return null if no collision
	}

	bool VerifyGrapplePoint()
	{
		Vector3 grappleDirection = (grapplePoint.position - hookRay.GlobalPosition).Normalized();

		Vector3? currentGrappleRayHit = Raycast(hookRay.GlobalPosition, grapplePoint.position + grappleDirection * 0.01f);

		if (currentGrappleRayHit == null)	//IF GRAPPLE POINT NOT ON WALL
		{
			/* CHECK FOR REVERTING TO LAST GRAPPLE POINT (COMMENTED OUT FOR NOW)
			GrapplePoint currentPoint = grapplePoint; // Store the current grapple point before updating
			while (currentPoint.lastGrapplePoint != null)
			{
				Vector3 oldGrappleDirection = (currentPoint.lastGrapplePoint.position - hookRay.GlobalPosition).Normalized();
				Vector3? lastGrappleRayHit = Raycast(hookRay.GlobalPosition, currentPoint.lastGrapplePoint.position + oldGrappleDirection * 0.1f);
				if (lastGrappleRayHit != null && lastGrappleRayHit.Value.DistanceTo(currentPoint.lastGrapplePoint.position) < 0.1f)
				{
					grapplePoint = currentPoint.lastGrapplePoint; // Revert to the last grapple point
					grappleSwingLength = GlobalPosition.DistanceTo(grapplePoint.position); // Update the swing length based on the last grapple point
					return true; // Reverted to last grapple point successfully
				}
				currentPoint = currentPoint.lastGrapplePoint; // Move to the last grapple point
			}
			*/

			if (isGrapplePulling)
			{
				StopGrapplePull();
			}
			if (isGrappleSwinging)
			{
				StopGrappleSwing();
			}
			return false; // No valid grapple point found, stop grappling
		}

		if (currentGrappleRayHit.Value.DistanceTo(grapplePoint.position) > 0.01f)	//IF NEW GRAPPLE POINT FOUND
		{
			GrapplePoint oldGrapplePoint = grapplePoint; // Store the current grapple point before updating
			grapplePoint = new GrapplePoint
			{
				position = currentGrappleRayHit.Value,
				lastGrapplePoint = oldGrapplePoint // Set the last grapple point to the previous one
			};
			grappleSwingLength = GlobalPosition.DistanceTo(grapplePoint.position); // Update the swing length based on the new grapple point

			// Calculate grapple normal to determine later if we should revert to the last grapple point
			Vector3 oldPosition = hookRay.GlobalPosition - Velocity; // Estimate the old position based on current velocity (Might need to multiply by delta)
			Vector3 vectorToOldGrapple = oldGrapplePoint.position - oldPosition;
			Vector3 vectorToNewGrapple = grapplePoint.position - oldPosition;

			grapplePoint.grappleNormal = vectorToNewGrapple.Cross(vectorToOldGrapple).Normalized(); // Calculate the normal vector between the old and new grapple points

			return true; // Valid grapple point found, continue grappling
		}

		if (grapplePoint.lastGrapplePoint != null)	//CHECK TO REVERT TO LAST GRAPPLE POINT
		{
			Vector3 oldGrappleDirection = (grapplePoint.lastGrapplePoint.position - hookRay.GlobalPosition).Normalized();
			Vector3? lastGrappleRayHit = Raycast(hookRay.GlobalPosition, grapplePoint.lastGrapplePoint.position + oldGrappleDirection * 0.1f);
			if (lastGrappleRayHit != null && lastGrappleRayHit.Value.DistanceTo(grapplePoint.lastGrapplePoint.position) < 0.1f)
			{
				// Grapple Normal check to see if we should revert to the last grapple point
				Vector3 vectorToLastGrapple = grapplePoint.lastGrapplePoint.position - hookRay.GlobalPosition;
				Vector3 vectorToCurrentGrapple = grapplePoint.position - hookRay.GlobalPosition;
				Vector3 grappleNormal = vectorToCurrentGrapple.Cross(vectorToLastGrapple).Normalized();
				if (grappleNormal.Dot(grapplePoint.grappleNormal) > 0)
				{
					grapplePoint = grapplePoint.lastGrapplePoint; // Revert to the last grapple point
					grappleSwingLength = GlobalPosition.DistanceTo(grapplePoint.position); // Update the swing length based on the last grapple point
					return true; // Reverted to last grapple point successfully
				}
				//GD.Print("Grapple Normal check failed, not reverting to last grapple point.");
			}
		}
		return true; // No issues with the current grapple point, continue grappling
	}

	Vector3 HandleGrappleSwing(Vector3 velocity, double delta)
	{
		if (!VerifyGrapplePoint()) // Ensure the grapple point is valid
		{
			return velocity; // If not valid, return the original velocity
		}

		float pullFactor = GlobalPosition.DistanceTo(grapplePoint.position) / grappleSwingLength; // Calculate how far the player is from the grapple point
		Vector3 grappleDirection = (grapplePoint.position - GlobalPosition).Normalized();
		float velocityAgainstGrapple = velocity.Dot(grappleDirection);

		if (isGrappleAscending)
		{
			grappleSwingLength -= grappleAscendSpeed * (float)delta; // Reduce the swing length to simulate ascending
			grappleSwingLength = Mathf.Max(grappleSwingLength, 1.0f); // Prevent negative swing length
			if (pullFactor > 1)
			{
				velocity -= velocityAgainstGrapple * grappleDirection;
				// Apply upward force while ascending
				velocity += grappleDirection * grappleAscendSpeed;
				return velocity; // Return early to avoid applying downward force
			}
		}
		else if (isGrappleDescending && GetFullGrappleLength() < maxGrappleExtension)
		{
			grappleSwingLength += grappleAscendSpeed * (float)delta; // Increase the swing length to simulate descending
			if (velocityAgainstGrapple < 0)
			{
				velocity -= velocityAgainstGrapple * grappleDirection;
				velocity += Math.Max(-grappleAscendSpeed, velocityAgainstGrapple) * grappleDirection; // Apply downward force while descending
				return velocity; // Return early to avoid applying upward force
			}
		}

		if (velocityAgainstGrapple < 0)
		{
			velocity -= velocityAgainstGrapple * grappleDirection * pullFactor * pullFactor * pullFactor; // Set the player's velocity to swing around the grapple point
		}
		//GD.Print($"Grapple Direction: {grappleDirection}, Velocity Against Grapple: {velocityAgainstGrapple}, Pull Factor: {pullFactor}");
		return velocity;
	}
	Vector3 HandleGrapplePull(Vector3 velocity, double delta)
	{
		if (!VerifyGrapplePoint())
		{
			return velocity; // If not valid, return the original velocity
		}

		if (GlobalPosition.DistanceTo(grapplePoint.position) <= grapplePullBreakDistance)
		{
			if (grapplePoint.lastGrapplePoint != null)
			{
				grapplePoint = grapplePoint.lastGrapplePoint; // Revert to the last grapple point if within break distance
			}
			else
			{
				return velocity * 0.9f;
			}
		}
		Vector3 grappleDirection = (grapplePoint.position - GlobalPosition).Normalized();
		float deceleration = grapplePullAcceleration / grapplePullSpeed; // Deceleration factor based on grapple pull speed and max speed

		velocity += grappleDirection * grapplePullAcceleration - velocity * deceleration; // Pull the player towards the collision point
		velocity.Y -= 9.8f * (float)delta; // Apply gravity while pulling

		return velocity;
	}
	Vector3 HandleGroundedMovement(Vector3 velocity, double delta)
	{
		Vector2 inputDir = Input.GetVector("MovementLeft", "MovementRight", "MovementForward", "MovementBackward");
		Vector3 direction = (GlobalBasis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		float deceleration = acceleration / groundSpeed; // Deceleration factor based on acceleration and max speed

		velocity.X += acceleration * direction.X - velocity.X * deceleration;
		velocity.Z += acceleration * direction.Z - velocity.Z * deceleration;

		if (inputDir.IsZeroApprox())
		{
			velocity.X = Mathf.MoveToward(velocity.X, 0, drag * (float)delta);
			velocity.Z = Mathf.MoveToward(velocity.Z, 0, drag * (float)delta);
		}

		if (Input.IsActionJustPressed("Jump"))
		{
			velocity.Y += jumpVelocity;
		}
		return velocity;
	}
	Vector3 HandleAerialMovement(Vector3 velocity, double delta)
	{
		Vector2 inputDir = Input.GetVector("MovementLeft", "MovementRight", "MovementForward", "MovementBackward");
		Vector3 direction = (GlobalBasis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		float deceleration = airAcceleration / airSpeed; // Deceleration factor for aerial movement

		velocity.X += direction.X * airAcceleration - velocity.X * deceleration;
		velocity.Z += direction.Z * airAcceleration - velocity.Z * deceleration;

		if (inputDir.IsZeroApprox())
		{
			velocity.X = Mathf.MoveToward(velocity.X, 0, airDrag * (float)delta);
			velocity.Z = Mathf.MoveToward(velocity.Z, 0, airDrag * (float)delta);
		}

		velocity += GetGravity() * (float)delta; // Apply gravity

		return velocity;
	}
	void HandleCollisions()
	{
		int collisionCount = GetSlideCollisionCount();
		if (collisionCount <= 0) return;

		for (int i = 0; i < collisionCount; i++)
		{

		}
	}
	public override void _Process(double delta)
	{
		terrainGeneration.GenerateFrom(GlobalPosition);
		HandleTerraformingInput(delta);
		HandleGrapplingHookInput(delta);
	}
	void HandleTerraformingInput(double delta)
	{
		if (Input.IsActionPressed("Break"))
		{
			if (interactionRay.IsColliding())
			{
				Vector3 collisionPoint = interactionRay.GetCollisionPoint();
				terrainGeneration.TerraformAt(collisionPoint, terraformRadius, -terraformSpeed * (float)delta);
			}
		}
		if (Input.IsActionPressed("Build"))
		{
			if (interactionRay.IsColliding())
			{
				Vector3 collisionPoint = interactionRay.GetCollisionPoint();
				if (collisionPoint.DistanceTo(GlobalPosition) < terraformRadius + 0.5f)
				{
					return; // Prevent building too close to the player
				}
				terrainGeneration.TerraformAt(collisionPoint, terraformRadius, terraformSpeed * (float)delta);
			}
		}
	}
	void HandleGrapplingHookInput(double delta)
	{
		isGrappleAscending = false; // Reset ascending flag
		isGrappleDescending = false; // Reset descending flag
		if (!isGrappleSwinging && Input.IsActionJustPressed("GrapplePull"))
		{
			if (hookRay.IsColliding())
			{
				grapplePoint.position = hookRay.GetCollisionPoint();
				StartGrapplePull();
			}
		}
		if (isGrapplePulling && Input.IsActionJustReleased("GrapplePull"))
		{
			StopGrapplePull();
		}
		if (!isGrapplePulling && Input.IsActionJustPressed("GrappleSwing"))
		{
			if (hookRay.IsColliding())
			{
				grapplePoint.position = hookRay.GetCollisionPoint();
				StartGrappleSwing();
			}
		}
		if (isGrappleSwinging && Input.IsActionJustReleased("GrappleSwing"))
		{
			StopGrappleSwing();
		}
		if (isGrapplePulling || isGrappleSwinging)
		{
			DrawGrapplingHook();
		}
		if (isGrappleSwinging)
		{
			if (Input.IsActionPressed("Jump"))
			{
				isGrappleAscending = true; // Set the ascending flag
			}
			if (Input.IsActionPressed("Crouch"))
			{
				isGrappleDescending = true; // Set the descending flag
			}
		}
	}
	void DrawGrapplingHook()
	{
		grappleOrigin.LookAt(grapplePoint.position, Basis.Y); // Orient the mesh towards the grapple point
		grappleOrigin.Scale = new Vector3(1.0f, 1.0f, grapplePoint.position.DistanceTo(grappleOrigin.Position)); // Scale the mesh based on distance to grapple point
	}
	void StartGrapplePull()
	{
		isGrapplePulling = true; // Set the grapple pulling flag
		grapplingHookMesh.Visible = true; // Show the grappling hook mesh
	}
	void StopGrapplePull()
	{
		isGrapplePulling = false; // Reset the grapple pulling flag
		grapplingHookMesh.Visible = false; // Hide the grappling hook mesh
		grapplePoint = new GrapplePoint(); // Reset the grapple point
	}
	void StartGrappleSwing()
	{
		airSpeed = grapplePullSpeed;
		isGrappleSwinging = true; // Set the grapple swinging flag
		grapplingHookMesh.Visible = true; // Show the grappling hook mesh
		grappleSwingLength = GlobalPosition.DistanceTo(grapplePoint.position); // Update the swing length based on the grapple point
	}
	void StopGrappleSwing()
	{
		airSpeed = groundSpeed; // Reset air speed to ground speed
		isGrappleSwinging = false; // Reset the grapple swinging flag
		grapplingHookMesh.Visible = false; // Hide the grappling hook mesh
		isGrappleAscending = false; // Reset ascending flag
		isGrappleDescending = false; // Reset descending flag
		grapplePoint = new GrapplePoint(); // Reset the grapple point
	}
	float GetFullGrappleLength()
	{
		if(grapplePoint == null)
		{
			return 0.0f; // Return 0 if there is no grapple point
		}
		float length = GlobalPosition.DistanceTo(grapplePoint.position); // Start with the distance to the current grapple point
		GrapplePoint currentPoint = grapplePoint;
		while (currentPoint.lastGrapplePoint != null)
		{
			length += currentPoint.position.DistanceTo(currentPoint.lastGrapplePoint.position);
			currentPoint = currentPoint.lastGrapplePoint; // Move to the last grapple point
		}
		return length; // Return the total length of the grapple chain
	}
}
