using UnityEngine;
using UnityEngine.InputSystem;

namespace DeathStar
{
    // First person inside the station, upright to the occupied room's floor (StationInteriors.Up), with direction
    // changes confined to connecting openings. Gravity is full in ordinary rooms, including the core;
    // explicitly authored zero-g rooms have none and the walker floats, WASD taking it where
    // it looks and Space and C (held) along its up. A lift's opening is floor to walk across, at the level of the floor
    // it is in. Space in a lift's column (StationInteriors.LiftAt) shoots the walker along it in rideTime to the next floor
    // up if it looks up, down if it looks down (from a lift's lowest floor, down into the hub if the lift starts there).
    // Sliding doors open as it comes near (StationInteriors.TickDoors, in play). It steps up stairs and lips up to
    // stepHeight and collides with its own scene's colliders (the rooms', their glass and the door leaves). It moves in
    // fixed steps of FixedStep (Frame), so it walks, falls and rides the same at any frame rate; looking turns at once,
    // each frame, and the camera is placed between the last two steps. The transform is the feet, its up the walker's
    // up and its forward the facing; the eye camera follows a smoothed frame. (MagBootWalker stays for walking the hull
    // outside, where there is no up.)
    public sealed class StationWalker : MonoBehaviour
    {
        public Camera eye;
        public StationInteriors interiors;
        public float radius=0.3f,height=1.8f,eyeHeight=1.65f,stepHeight=0.32f;
        public float walkSpeed=1.5f,runSpeed=3.2f,floatSpeed=1.2f,lookSpeed=0.1f,gravity=9.81f,rideTime=0.5f;
        // Degrees a second the body turns towards the local up at most: walking it follows the core's direction well
        // within this; it only shows after floating out of the hub.
        public float rightingSpeed=90;
        // Everything but Ignore Raycast, the walker's own layer.
        public LayerMask mask=~(1<<2);
        public const float FixedStep=1f/120;

        public enum Mode{Walk,Fall,Lift,Float}
        public Mode State {get;private set;}
        public Vector3 Velocity=>velocity;
        public Vector3 EyePosition=>transform.position+transform.up*eyeHeight;
        public Quaternion EyeRotation=>transform.rotation*Quaternion.Euler(pitch,0,0);

        const float Skin=0.02f,Snap=0.3f,Lip=0.08f,FloorCos=0.6f,Probe=0.25f;
        // The ends of the capsule's axis above the feet: the whole body on foot, the head alone when floating, so a body
        // that can't turn to the way out of a zero-g room still goes through any door or hatch in it.
        float Low=>State==Mode.Float?eyeHeight-0.2f:Lip+radius;
        float High=>State==Mode.Float?eyeHeight+0.2f:height-radius;
        const QueryTriggerInteraction Triggers=QueryTriggerInteraction.Ignore;
        Vector3 velocity;float pitch;
        CapsuleCollider body;
        readonly Collider[] overlaps=new Collider[16];
        // The camera: a frame that eases after the body's turns, and an offset that eases out the body's jumps.
        Quaternion frame=Quaternion.identity;Vector3 offset;
        // Fixed steps: time not yet stepped, the feet before the last step and how far the camera is past them towards
        // the feet now, and a Space press waiting for the next step.
        float accumulator,blend=1;Vector3 previous;bool launch;
        // A ride in progress: its lift, from and to (along its axis), how long it has run, the feet's offsets from the
        // axis at its ends, and whether it ends floating in the hub.
        StationInteriors.Lift ride;float rideFrom,rideTo,rideT;Vector3 rideStart,rideEnd,rideUp;bool rideToHub;

        PhysicsScene World=>gameObject.scene.GetPhysicsScene();

        void Awake()=>Ensure();
        void Ensure()
        {
            if(body!=null)return;
            body=GetComponent<CapsuleCollider>();if(!body)body=gameObject.AddComponent<CapsuleCollider>();
            body.isTrigger=true;body.radius=radius;body.height=height-Lip;body.center=new Vector3(0,(height+Lip)*0.5f,0);body.direction=1;
            gameObject.layer=2;
        }

        // Stands at feet with up, facing forward, looking level.
        public void Place(Vector3 feet,Vector3 up,Vector3 forward)
        {
            Ensure();
            transform.SetPositionAndRotation(feet,Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,up),up));
            pitch=0;velocity=Vector3.zero;State=Mode.Walk;frame=transform.rotation;offset=Vector3.zero;
            previous=feet;blend=1;accumulator=0;launch=false;ride=null;
        }
        // Turns the head (and body about its up) to look at a point.
        public void LookAt(Vector3 target)
        {
            var d=target-EyePosition;var flat=Vector3.ProjectOnPlane(d,transform.up);
            if(flat.sqrMagnitude>1e-6f)transform.rotation=Quaternion.LookRotation(flat,transform.up);
            pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized,transform.up),-1,1))*Mathf.Rad2Deg,-89,89);
            frame=transform.rotation;
        }

        void Update()
        {
            var kb=Keyboard.current;var mouse=Mouse.current;
            Vector2 move=default,look=default;float vertical=0;bool run=false,space=false;
            if(kb!=null)
            {
                if(kb.wKey.isPressed)move.y+=1;if(kb.sKey.isPressed)move.y-=1;
                if(kb.dKey.isPressed)move.x+=1;if(kb.aKey.isPressed)move.x-=1;
                if(kb.spaceKey.isPressed)vertical+=1;if(kb.cKey.isPressed||kb.leftCtrlKey.isPressed)vertical-=1;
                space=kb.spaceKey.wasPressedThisFrame;run=kb.leftShiftKey.isPressed;
            }
            if(mouse!=null&&Cursor.lockState==CursorLockMode.Locked)look=mouse.delta.ReadValue()*lookSpeed;
            Frame(move,look,space,vertical,run,Time.deltaTime);
        }
        void LateUpdate()
        {
            if(eye==null)return;
            Ease(Time.deltaTime);
            eye.transform.SetPositionAndRotation(ViewPosition,ViewRotation);
        }
        // The camera's eased pose (the lab renders from it too).
        public Vector3 ViewPosition=>Vector3.Lerp(previous,transform.position,blend)+offset+transform.up*eyeHeight;
        public Quaternion ViewRotation=>frame*Quaternion.Euler(pitch,0,0);
        public void Ease(float dt)
        {
            float k=1-Mathf.Exp(-dt*10);
            frame=Quaternion.Slerp(frame,transform.rotation,k);offset*=Mathf.Exp(-dt*8);
        }

        // One rendered frame of dt: the look (yaw and pitch in degrees) turns at once; moving runs in as many fixed steps
        // as the time adds up to (at most a quarter of a second's worth), with move across the floor (x right, y
        // forward), space (Space pressed this frame: a lift's ride), vertical (+1 up, -1 down: floating) and run.
        public void Frame(Vector2 move,Vector2 look,bool space,float vertical,bool run,float dt)
        {
            Ensure();
            Look(look);launch|=space;
            accumulator+=Mathf.Min(dt,0.25f);
            while(accumulator>=FixedStep){Step(move,Vector2.zero,launch,vertical,run,FixedStep);launch=false;accumulator-=FixedStep;}
            blend=accumulator/FixedStep;
        }

        void Look(Vector2 look)
        {
            var yaw=Quaternion.Euler(0,look.x,0);transform.rotation*=yaw;frame*=yaw;
            pitch=Mathf.Clamp(pitch-look.y,-89,89);
        }

        // One step of dt (Frame runs them at FixedStep; the lab may call it directly).
        public void Step(Vector2 move,Vector2 look,bool space,float vertical,bool run,float dt)
        {
            Ensure();previous=transform.position;blend=1;
            Look(look);
            if(ride!=null){Ride(dt);return;}
            var mid=transform.position+transform.up*(height*0.5f);
            // floating, what counts is where the head is (the body is only that, Low and High); on foot it floats again
            // only once head and middle are both out of gravity, so it doesn't flicker in a doorway
            bool floating=State==Mode.Float;
            if(floating)mid=EyePosition;
            float g=interiors!=null?interiors.Gravity(mid):1;
            if(interiors!=null&&!floating&&g<=0.01f)g=interiors.Gravity(EyePosition);
            // ... and it lands once the head is well past the way out, where there's floor under it
            if(interiors!=null&&floating&&g>0.01f&&velocity.sqrMagnitude>0.01f)g=Mathf.Min(g,interiors.Gravity(mid-velocity.normalized*0.8f));
            if(floating&&g>0.01f)
            {
                // Stay compact until upright and clear of the hatch; never expand inside a wall.
                if(!Land(dt)){Float(move,vertical,dt);return;}
                mid=transform.position+transform.up*(height*0.5f);
            }
            if(g>0.01f)Right(interiors!=null?interiors.Up(mid):Vector3.up,dt);
            var lift=interiors!=null?interiors.LiftAt(mid):null;
            if(space&&lift!=null&&Launch(lift)){Ride(dt);return;}
            float speed=run?runSpeed:walkSpeed;
            if(g<=0.01f)Float(move,vertical,dt);
            else Walk((transform.right*move.x+transform.forward*move.y)*speed,g,lift,dt);
        }

        // Turns the body's up towards up about the feet, at most rightingSpeed a second.
        void Right(Vector3 up,float dt)
        {
            if(up.sqrMagnitude<0.5f)return;
            var from=transform.up;float angle=Vector3.Angle(from,up);if(angle<1e-3f)return;
            var axis=Vector3.Cross(from,up);if(axis.sqrMagnitude<1e-8f)axis=transform.right;
            transform.rotation=Quaternion.AngleAxis(Mathf.Min(angle,rightingSpeed*dt),axis.normalized)*transform.rotation;
        }

        // Walking: v across the floor, slid along walls and stepping up onto what is low enough, and gravity g (a share
        // of full) along -up, landing on anything level enough to stand on, or on the floor a lift's opening is in.
        void Walk(Vector3 v,float g,StationInteriors.Lift lift,float dt)
        {
            var up=transform.up;bool grounded=State==Mode.Walk;
            float vy=Vector3.Dot(velocity,up);if(grounded&&vy<0)vy=0;
            vy-=gravity*g*dt;
            var delta=Vector3.ProjectOnPlane(v,up)*dt;
            if(delta.sqrMagnitude>1e-10f)Move(delta,grounded,lift);
            float dy=vy*dt,reach=-dy+(grounded?Snap:0);
            if(dy>0){Slide(up*dy,false);State=Mode.Fall;}
            else if(Stand(reach,lift,out var feet))
            {
                if(grounded)SmoothHeight(Vector3.Dot(feet-transform.position,up));
                transform.position=feet;vy=0;State=Mode.Walk;
            }
            else{transform.position+=up*dy;State=Mode.Fall;}
            velocity=up*vy+Vector3.ProjectOnPlane(v,up);
            Depenetrate();
        }

        // Where the feet stand within reach: on what is under them (Ground), or, over a lift's opening, on the floor it is
        // cut in, carried on across it (StationInteriors.FloorUnder), whichever is higher.
        bool Stand(float reach,StationInteriors.Lift lift,out Vector3 feet)
        {
            bool on=Ground(reach,out feet);
            if(lift==null)return on;
            var up=transform.up;var p=transform.position;
            for(int i=0;i<lift.stops.Count;i++)
            {
                if(Mathf.Abs(Vector3.Dot(lift.stops[i]-p,up))>2)continue;
                var level=interiors.FloorUnder(lift.stopCells[i],p);float d=Vector3.Dot(level-p,up);
                if(d<-reach-Skin||d>Probe)continue;
                level=p+up*d;
                if(!on||Vector3.Dot(level-feet,up)>0){feet=level;on=true;}
            }
            return on;
        }

        // Across the floor by delta; where something blocks it on the floor, again a step up, settling back onto the step.
        void Move(Vector3 delta,bool grounded,StationInteriors.Lift lift)
        {
            var start=transform.position;var dir=delta.normalized;
            Slide(delta,true);
            if(!grounded)return;
            float got=Vector3.Dot(transform.position-start,dir);if(got>delta.magnitude*0.6f)return;
            var up=transform.up;var blocked=transform.position;
            float step=stepHeight;
            if(World.CapsuleCast(start+up*(radius+Lip),start+up*(height-radius),radius-Skin,up,out var overhead,stepHeight+Skin,mask,Triggers))
                step=Mathf.Min(step,Mathf.Max(0,overhead.distance-Skin));
            if(step<0.02f)return;
            transform.position=start+up*step;
            Slide(delta,true);
            if(Vector3.Dot(transform.position-start,dir)>got+1e-4f&&Stand(step+Skin,lift,out var feet))
            {SmoothHeight(Vector3.Dot(feet-start,up));transform.position=feet;}
            else transform.position=blocked;
        }

        void Slide(Vector3 delta,bool level)
        {
            var up=transform.up;
            for(int i=0;i<3&&delta.sqrMagnitude>1e-10f;i++)
            {
                float len=delta.magnitude;var dir=delta/len;var p=transform.position;
                if(World.CapsuleCast(p+up*Low,p+up*High,radius-Skin,dir,out var hit,len+Skin,mask,Triggers))
                {
                    float go=Mathf.Max(0,hit.distance-Skin);transform.position+=dir*go;
                    delta=Vector3.ProjectOnPlane(dir*(len-go),hit.normal);if(level)delta=Vector3.ProjectOnPlane(delta,up);
                }
                else{transform.position+=delta;break;}
            }
        }

        // Something to stand on within reach below the feet: where the feet go on it. The cast starts Probe above the
        // feet, so it still finds a floor the feet have sunk a little into (a cast misses what it starts inside), and
        // finds a step's top up to Probe above them. A sphere meets a step's nose, or the foot of a wall leaning over the
        // floor (on the curved grid), before the floor: a nose counts if what lies just past it is level, and beside a
        // wall a ray straight down from the middle finds the floor.
        bool Ground(float reach,out Vector3 feet)
        {
            var up=transform.up;var o=transform.position+up*(radius+Probe);feet=default;float far=reach+Skin+Probe;
            if(!World.SphereCast(o,radius-Skin,-up,out var hit,far,mask,Triggers))return false;
            if(Vector3.Dot(hit.normal,up)>=FloorCos)
            {
                feet=o-up*(hit.distance+radius-Skin);
                if(World.Raycast(o,-up,out var centre,far+radius-Skin,mask,Triggers)&&Vector3.Dot(centre.normal,up)>=FloorCos
                    &&Mathf.Abs(Vector3.Dot(centre.point-feet,up))<0.05f)feet=centre.point;
                return true;
            }
            var away=Vector3.ProjectOnPlane(hit.point-o,up);
            var past=hit.point+up*0.05f+(away.sqrMagnitude>1e-8f?away.normalized*0.03f:Vector3.zero);
            if(World.Raycast(past,-up,out var top,0.1f,mask,Triggers)&&Vector3.Dot(top.normal,up)>=FloorCos)
            {feet=transform.position+up*Vector3.Dot(top.point-transform.position,up);return true;}
            if(World.Raycast(o,-up,out var below,far+radius,mask,Triggers)&&Vector3.Dot(below.normal,up)>=FloorCos){feet=below.point;return true;}
            return false;
        }
        // What is under the feet, for the lab.
        public string Below()
        {
            var up=transform.up;var o=transform.position+up*(radius+Probe);
            return World.SphereCast(o,radius-Skin,-up,out var hit,2f,mask,Triggers)?$"{hit.collider.name} {hit.distance-Probe-Skin:0.000} m below (normal·up {Vector3.Dot(hit.normal,up):0.00})":"nothing below";
        }

        // Space in a lift's column: off along it to the nearest floor above if the walker looks up (or level), below if
        // it looks down; from the lowest floor of a lift that starts in the hub, down into the hub. False if there is no
        // floor that way.
        bool Launch(StationInteriors.Lift lift)
        {
            var up=transform.up;var feet=transform.position;
            bool rising=Vector3.Dot(EyeRotation*Vector3.forward,up)>=0;
            var to=Vector3.zero;float best=float.MaxValue;
            foreach(var s in lift.stops)
            {
                float d=Vector3.Dot(s-feet,up);
                if((rising?d>0.5f:d<-0.5f)&&Mathf.Abs(d)<best){best=Mathf.Abs(d);to=s;}
            }
            bool hub=best==float.MaxValue&&!rising&&lift.fromHub;
            if(hub)to=lift.axis[0];else if(best==float.MaxValue)return false;
            ride=lift;rideT=0;rideToHub=hub;rideFrom=lift.Along(feet);rideTo=lift.Along(to);
            rideUp=hub?transform.up:interiors.Up(to+interiors.Up(to)*0.9f);
            rideStart=feet-lift.At(rideFrom);rideEnd=to-lift.At(rideTo);
            State=Mode.Lift;velocity=Vector3.zero;
            return true;
        }

        // The ride: drawn in to the lift's middle over its first 30% (the openings are narrower than the column) while
        // the run along the axis, eased in and out, starts a little after it; landing on the floor (or floating in the
        // hub). It passes through nothing, so it sweeps nothing.
        void Ride(float dt)
        {
            Right(rideUp,dt);
            rideT+=dt;float t=Mathf.Clamp01(rideT/rideTime),a=Mathf.Clamp01((t-0.1f)/0.9f),e=a*a*(3-2*a),c=Mathf.SmoothStep(0,1,t/0.3f);
            transform.position=ride.At(Mathf.Lerp(rideFrom,rideTo,e))+rideStart*(1-c)+rideEnd*e;
            if(t<1)return;
            ride=null;State=rideToHub?Mode.Float:Mode.Walk;velocity=Vector3.zero;
            Depenetrate();
        }

        // In zero g: thrusting gently where it looks (and along its up), drifting on, slowing a little; what it runs into
        // takes up its speed into it.
        void Float(Vector2 move,float vertical,float dt)
        {
            State=Mode.Float;
            var look=EyeRotation;
            var want=(look*Vector3.forward*move.y+look*Vector3.right*move.x+transform.up*vertical)*floatSpeed;
            if(want.sqrMagnitude>1e-6f)velocity=Vector3.MoveTowards(velocity,want,2*dt);else velocity*=Mathf.Exp(-dt*0.5f);
            var before=transform.position;
            Slide(velocity*dt,false);
            if(dt>0)velocity=(transform.position-before)/dt;
            Depenetrate();
        }

        // Gravity again after floating: right gradually around the head, then expand only with full-body clearance.
        bool Land(float dt)
        {
            var eyeAt=EyePosition;
            var up=interiors!=null?interiors.Up(eyeAt):transform.up;
            if(up.sqrMagnitude<0.5f)up=transform.up;
            Right(up,dt);
            transform.position=eyeAt-transform.up*eyeHeight;previous=transform.position;
            if(Vector3.Angle(transform.up,up)>1)return false;
            var feet=eyeAt-up*eyeHeight;
            if(World.Raycast(eyeAt,-up,out var floor,eyeHeight,mask,Triggers)&&Vector3.Dot(floor.normal,up)>=FloorCos)
                feet=floor.point;
            int n=World.OverlapCapsule(feet+up*(Lip+radius),feet+up*(height-radius),radius-Skin,overlaps,mask,Triggers);
            if(n>0)return false;
            // Preserve the eye through any floor-height correction. Rotation eases independently of eye position.
            offset+=transform.position-feet;transform.position=feet;previous=feet;
            State=Mode.Fall;velocity=Vector3.ProjectOnPlane(velocity,up);
            return true;
        }

        void SmoothHeight(float rise)
        {
            if(Mathf.Abs(rise)<0.001f)return;
            offset-=transform.up*rise;
            // Several stair steps must not leave the eye trailing through the floor.
            offset=Vector3.ClampMagnitude(offset,stepHeight+Skin);
        }

        // Resolve contacts without teleporting the eye. Skin contact alone needs no correction.
        void SmoothCorrection(Vector3 correction)
        {
            if(correction.sqrMagnitude>1e-10f)
            {
                offset=Vector3.ClampMagnitude(offset-correction,stepHeight+Skin);
            }
        }

        // Out of anything the body overlaps, leaving out its lowest Lip, so it stands on lips rather than being pushed
        // off them sideways. Floating, the body is just round the head.
        void Depenetrate()
        {
            var before=transform.position;
            float h=High-Low+2*radius;body.height=h;body.center=new Vector3(0,(Low+High)*0.5f,0);
            for(int pass=0;pass<3;pass++)
            {
                var up=transform.up;var p=transform.position;bool moved=false;
                int n=World.OverlapCapsule(p+up*Low,p+up*High,radius,overlaps,mask,Triggers);
                for(int i=0;i<n;i++)
                {
                    var c=overlaps[i];if(c==body)continue;
                    if(Physics.ComputePenetration(body,transform.position,transform.rotation,c,c.transform.position,c.transform.rotation,out var dir,out float dist)&&dist>0.001f)
                    {transform.position+=dir*(dist+0.001f);moved=true;}
                }
                if(!moved)break;
            }
            SmoothCorrection(transform.position-before);
        }
    }
}
