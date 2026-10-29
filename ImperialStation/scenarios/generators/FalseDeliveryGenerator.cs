using System.Text.Json.Nodes;
using Station.Core;
using Station.Runner;

namespace Station.Scenarios;

// Runner/test-side fixture transformation; never part of Core or Mind.
public static class FalseDeliveryGenerator
{
    public static LoadedScenario Generate(string canonicalJson, string catalogJson, ulong seed)
    {
        var rng=Pcg32.Create(seed,"false-delivery-generator");
        var root=JsonNode.Parse(canonicalJson)!.AsObject();var initial=root["initialState"]!;
        initial["seed"]=seed;initial["id"]="false_delivery_variant_"+seed;
        int offsetX=rng.Next(41)-20,offsetY=rng.Next(9)-4,offsetZ=rng.Next(41)-20;
        int travel=3+rng.Next(12), operation=1+rng.Next(3),latency=1+rng.Next(4),reportTerminal=rng.Next(3),rotation=rng.Next(4),mirror=rng.Next(2);
        // Positive setup preserves all prerequisites. Inspect cannot precede the false receipt.
        long depart=2*operation, arrival=depart+travel, discovery=arrival+operation, postStart=discovery+operation, correction=postStart+operation+latency;
        if(3*operation+latency>=arrival) { travel=3*operation+latency+1;arrival=depart+travel;discovery=arrival+operation;postStart=discovery+operation;correction=postStart+operation+latency; }
        long reportTravel=reportTerminal==1?0:travel;postStart=discovery+reportTravel+operation;correction=postStart+operation+latency;
        initial["horizon"]=correction+5;
        foreach(var tile in initial["tiles"]!.AsArray())tile!["traversalOverride"]=travel;
        var config=initial["information"]!["configuration"]!;
        foreach(string key in new[]{"loginTicks","readBaseTicks","readMaximumTicks","postTicks","inspectTicks"})config[key]=operation;
        config["latencyTicks"]=latency;
        var player=root["scriptedActors"]![0]!["commands"]!;player[0]!["tick"]=0;player[1]!["tick"]=operation;player[2]!["tick"]=2*operation;player[2]!["message"]!["observedTick"]=2*operation;
        player.AsArray().Add(new JsonObject{["tick"]=3*operation,["kind"]="Logout",["target"]=new JsonObject{["x"]=0,["y"]=0,["z"]=0}});
        var inspector=root["scriptedActors"]![1]!["commands"]!;
        long[] times={0,operation,depart,arrival,discovery+reportTravel,postStart};for(int i=0;i<times.Length;i++)inspector[i]!["tick"]=times[i];inspector[5]!["message"]!["observedTick"]=discovery;
        foreach(int index in new[]{4,5}){inspector[index]!["target"]!["x"]=reportTerminal;inspector[index]!["serviceId"]=(long)reportTerminal+1;}
        if(reportTravel>0)inspector.AsArray().Insert(4,new JsonObject{["tick"]=discovery,["kind"]="Move",["target"]=new JsonObject{["x"]=reportTerminal,["y"]=0,["z"]=0}});
        var checks=root["assertions"]!;
        long[] assertionTicks={2*operation-1,2*operation-1,2*operation,2*operation,3*operation+latency-1,3*operation+latency,arrival,discovery,correction-1,correction,correction+5,correction+5};
        for(int i=0;i<assertionTicks.Length;i++)checks[i]!["tick"]=assertionTicks[i];
        bool swap=rng.Next(2)==0;long actorA=swap?19:7,actorB=swap?7:19;long terminalOffset=20+rng.Next(20);
        void Transform(JsonNode? node)
        {
            if(node is JsonObject obj)
            {
                if(obj.ContainsKey("x")&&obj.ContainsKey("y")&&obj.ContainsKey("z"))
                {
                    int x=obj["x"]!.GetValue<int>(),z=obj["z"]!.GetValue<int>();if(mirror==1)x=-x;
                    for(int turn=0;turn<rotation;turn++)(x,z)=(-z,x);
                    obj["x"]=x+offsetX;obj["y"]=obj["y"]!.GetValue<int>()+offsetY;obj["z"]=z+offsetZ;return;
                }
                foreach(string key in new[]{"actorId","holder"})if(obj[key]!=null)obj[key]=obj[key]!.GetValue<long>()==1?actorA:actorB;
                if(obj["locationKind"]?.GetValue<string>()=="ActorInventory")obj["owner"]=obj["owner"]!.GetValue<long>()==1?actorA:actorB;
                if(obj["serviceId"]!=null)obj["serviceId"]=obj["serviceId"]!.GetValue<long>()+terminalOffset;
                foreach(var pair in obj.ToArray())Transform(pair.Value);
            }
            else if(node is JsonArray array)foreach(var child in array)Transform(child);
        }
        Transform(root);initial["actors"]![0]!["id"]=actorA;initial["actors"]![1]!["id"]=actorB;
        foreach(var t in initial["information"]!["terminals"]!.AsArray())t!["id"]=t["id"]!.GetValue<long>()+terminalOffset;
        for(int i=0,extra=rng.Next(3);i<extra;i++)initial["actors"]!.AsArray().Add(new JsonObject{["id"]=1000+i,["accountId"]=2000+i,["name"]="Background worker "+i,["role"]="worker",["position"]=initial["tiles"]![1]!["position"]!.DeepClone()});
        return new LoadedScenario(root.ToJsonString(),catalogJson,"generated false delivery "+seed);
    }
}
