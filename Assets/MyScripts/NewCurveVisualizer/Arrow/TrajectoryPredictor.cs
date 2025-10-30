using UnityEngine;

public class TrajectoryPredictor : MonoBehaviour
{
    [Header("Trajectory Settings")]
    [SerializeField] private int maxPoints = 50;
    [SerializeField] private float timeStep = 0.1f;
    [SerializeField] private float maxTime = 5f;
    [SerializeField] private LayerMask layerMask=0; // Layers to consider for collision
    /// <summary>
    /// Predicts and draws the trajectory path using physics calculations
    /// </summary>
    /// <param name="startPosition">Starting position of the projectile</param>
    /// <param name="velocity">Initial velocity vector</param>
    /// <param name="lineRenderer">LineRenderer to draw the trajectory</param>
    /// <param name="points">Number of points to calculate</param>
    /// <param name="timeStep">Time step between calculations</param>
    /// <param name="gravityMultiplier">Multiplier for gravity to adjust curve</param>
    public void PredictTrajectory(Vector3 startPosition, Vector3 velocity, LineRenderer lineRenderer, int points, float timeStep, float gravityMultiplier = 1f)
    {
        Vector3[] trajectoryPoints = new Vector3[points];
        Vector3 currentPosition = startPosition;
        Vector3 currentVelocity = velocity;
        
        // Apply gravity multiplier to create custom curvature
        Vector3 customGravity = Physics.gravity * gravityMultiplier;
        
        for (int i = 0; i < points; i++)
        {
            trajectoryPoints[i] = currentPosition;
            
            // Calculate next position using physics equations
            float time = i * timeStep;
            
            // Check if we've exceeded max time
            if (time > maxTime)
            {
                // Resize array to current point
                Vector3[] trimmedPoints = new Vector3[i];
                System.Array.Copy(trajectoryPoints, trimmedPoints, i);
                trajectoryPoints = trimmedPoints;
                break;
            }
            
            // Physics calculation with custom gravity: position = initial_position + velocity * time + 0.5 * gravity * time^2
            Vector3 displacement = velocity * time + 0.5f * customGravity * time * time;
            Vector3 nextPosition = startPosition + displacement;
            
            // Check for ground collision (simple ground check at y = 0)
            if (nextPosition.y <= 0 && i > 0)
            {
                // Calculate exact intersection point with ground using custom gravity
                float groundIntersectionTime = CalculateGroundIntersectionTime(startPosition, velocity, customGravity);
                if (groundIntersectionTime > 0)
                {
                    Vector3 groundPoint = startPosition + velocity * groundIntersectionTime + 0.5f * customGravity * groundIntersectionTime * groundIntersectionTime;
                    groundPoint.y = 0; // Ensure it's exactly on the ground
                    trajectoryPoints[i] = groundPoint;
                }
                
                // Resize array to include ground point
                Vector3[] trimmedPoints = new Vector3[i + 1];
                System.Array.Copy(trajectoryPoints, trimmedPoints, i + 1);
                trajectoryPoints = trimmedPoints;
                break;
            }
            
            // Check for obstacle collision
            if (i > 0)
            {
                Vector3 previousPosition = trajectoryPoints[i - 1];
                if (CheckTrajectoryCollision(previousPosition, nextPosition, out Vector3 hitPoint))
                {
                    trajectoryPoints[i] = hitPoint;
                    
                    // Resize array to include collision point
                    Vector3[] trimmedPoints = new Vector3[i + 1];
                    System.Array.Copy(trajectoryPoints, trimmedPoints, i + 1);
                    trajectoryPoints = trimmedPoints;
                    break;
                }
            }
            
            currentPosition = nextPosition;
        }
        
        // Apply points to LineRenderer
        lineRenderer.positionCount = trajectoryPoints.Length;
        lineRenderer.SetPositions(trajectoryPoints);
    }
    
    /// <summary>
    /// Original method for backward compatibility
    /// </summary>
    public void PredictTrajectory(Vector3 startPosition, Vector3 velocity, LineRenderer lineRenderer, int points, float timeStep)
    {
        PredictTrajectory(startPosition, velocity, lineRenderer, points, timeStep, 1f);
    }
    
    /// <summary>
    /// Calculates the exact time when the projectile hits the ground (y = 0)
    /// </summary>
    private float CalculateGroundIntersectionTime(Vector3 startPosition, Vector3 velocity, Vector3 gravity)
    {
        // Solve quadratic equation: y = y0 + vy*t + 0.5*g*t^2 = 0
        // 0.5*g*t^2 + vy*t + y0 = 0
        float a = 0.5f * gravity.y;
        float b = velocity.y;
        float c = startPosition.y;
        
        // Quadratic formula: t = (-b ± sqrt(b^2 - 4ac)) / 2a
        float discriminant = b * b - 4 * a * c;
        
        if (discriminant < 0)
            return -1; // No intersection
        
        float sqrtDiscriminant = Mathf.Sqrt(discriminant);
        float t1 = (-b + sqrtDiscriminant) / (2 * a);
        float t2 = (-b - sqrtDiscriminant) / (2 * a);
        
        // Return the positive time that's not the starting time
        if (t1 > 0.01f && t2 > 0.01f)
            return Mathf.Min(t1, t2);
        else if (t1 > 0.01f)
            return t1;
        else if (t2 > 0.01f)
            return t2;
        
        return -1;
    }
    
    /// <summary>
    /// Original method for backward compatibility
    /// </summary>
    private float CalculateGroundIntersectionTime(Vector3 startPosition, Vector3 velocity)
    {
        return CalculateGroundIntersectionTime(startPosition, velocity, Physics.gravity);
    }
    
    /// <summary>
    /// Checks for collision along the trajectory path
    /// </summary>
    private bool CheckTrajectoryCollision(Vector3 start, Vector3 end, out Vector3 hitPoint)
    {
        hitPoint = end;
        
        Vector3 direction = end - start;
        float distance = direction.magnitude;
        
        if (distance < 0.01f)
            return false;
        
        direction = direction.normalized;
        
        // Raycast between trajectory points
        RaycastHit hit;
        if (Physics.Raycast(start, direction, out hit, distance,layerMask))
        {
            // Ignore projectile objects and triggers
            if (hit.collider.GetComponent<ArrowFollowTrajectory>() == null && !hit.collider.isTrigger)
            {
                hitPoint = hit.point;
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Simulates the complete trajectory and returns the landing position
    /// </summary>
    /// <param name="startPosition">Starting position</param>
    /// <param name="velocity">Initial velocity</param>
    /// <returns>Final landing position</returns>
    public Vector3 SimulateTrajectory(Vector3 startPosition, Vector3 velocity)
    {
        Vector3 currentPosition = startPosition;
        float currentTime = 0f;
        
        while (currentTime < maxTime)
        {
            currentTime += timeStep;
            
            // Calculate position at current time
            Vector3 displacement = velocity * currentTime + 0.5f * Physics.gravity * currentTime * currentTime;
            Vector3 newPosition = startPosition + displacement;
            
            // Check for ground collision
            if (newPosition.y <= 0)
            {
                float groundTime = CalculateGroundIntersectionTime(startPosition, velocity);
                if (groundTime > 0)
                {
                    Vector3 groundPoint = startPosition + velocity * groundTime + 0.5f * Physics.gravity * groundTime * groundTime;
                    groundPoint.y = 0;
                    return groundPoint;
                }
                return newPosition;
            }
            
            // Check for obstacle collision
            if (CheckTrajectoryCollision(currentPosition, newPosition, out Vector3 hitPoint))
            {
                return hitPoint;
            }
            
            currentPosition = newPosition;
        }
        
        return currentPosition;
    }
}