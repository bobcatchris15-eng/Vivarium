#!/usr/bin/env python3
"""Idempotent, source-auditable lower-fauna-density calibration. --check is read-only.
Only writes fauna species ecology and fauna budgets/starter counts in world presets.
"""
import argparse
import json
import math
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
BASE=Path(__file__).with_name("fauna_calibration_baseline.json")
FAUNA=ROOT/"game/content/fauna"
PRESETS=ROOT/"game/content/presets"

def load(p):
    return json.loads(p.read_text(encoding="utf-8"))

def write(p,obj):
    p.write_text(json.dumps(obj,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")

def freeze():
    result={"version":1,"species":{},"presets":{}}
    for p in sorted(FAUNA.glob("*.json")):
        obj=load(p)
        result["species"][p.stem]={
            "lifecycle":{k:obj["lifecycle"][k] for k in
                ("maturityDays","lifespanDays","dailyMortality")},
            "reproduction":{k:obj["reproduction"][k] for k in
                ("mode","clutchMin","clutchMax","cooldownDays","maxLocalDensity","populationCap")},
            "guild":obj["guild"]}
    for p in sorted(PRESETS.glob("*.json")):
        obj=load(p)
        result["presets"][p.stem]={"starterFauna":obj.get("starterFauna",[]),
                                   "diameter":obj["diameter"]}
    return result

def targets(original):
    life=original["lifecycle"]
    rep=original["reproduction"]
    predator=original["guild"]=="predator"
    return {"lifecycle":{
        "maturityDays":round(life["maturityDays"]*1.40,4),
        "lifespanDays":round(life["lifespanDays"]*2.0,4),
        "dailyMortality":round(life["dailyMortality"]*.50,6)},
       "reproduction":{
        "clutchMin":1,
        "clutchMax":max(1,round(rep["clutchMax"]*.55)),
        "cooldownDays":round(rep["cooldownDays"]*2.5,4),
        "maxLocalDensity":max(3 if predator else 4,round(rep["maxLocalDensity"]*.67)),
        "populationCap":max(8 if predator else 10,round(rep["populationCap"]*.32))}}

def fauna_budget(diameter):
    return min(440,max(210,round(360*(diameter/8.0)**.8)))

def founders(rows,species):
    new=[]
    for item in rows:
        old=item["count"]
        fauna=species[item["species"]]
        sexual=fauna["reproduction"]["mode"]=="sexual"
        # Rare apex pairs are too fragile: in the native run one of two
        # stonebells died naturally and instantly stranded its partner.
        # Preserve a third founder when the original preset had one.
        floor=(3 if fauna["guild"]=="predator" and old>=3 else
               2 if sexual and old>=2 else (1 if old else 0))
        new.append({**item,"count":min(old,max(floor,round(old*.55)))})
    return new

def run(check):
    if not BASE.is_file():
        if check:raise RuntimeError("No frozen fauna baseline; run --apply first")
        write(BASE,freeze())
    base=load(BASE)
    for name,original in base["species"].items():
        p=FAUNA/(name+".json")
        obj=load(p)
        new=targets(original)
        for block,fields in new.items():
            for key,value in fields.items():
                if check:
                    assert obj[block][key]==value,(name,block,key,obj[block][key],value)
                else:
                    obj[block][key]=value
        if not check:write(p,obj)
        print("SPECIES",name,"cap",new["reproduction"]["populationCap"],
              "life",new["lifecycle"]["lifespanDays"],
              "cooldown",new["reproduction"]["cooldownDays"])
    for name,original in base["presets"].items():
        p=PRESETS/(name+".json")
        obj=load(p)
        budget=fauna_budget(original["diameter"])
        initial=founders(original["starterFauna"],base["species"])
        if check:
            assert obj.get("faunaBudget")==budget,name
            assert obj.get("starterFauna")==initial,name
        else:
            obj["faunaBudget"]=budget
            obj["starterFauna"]=initial
            write(p,obj)
        print("PRESET",name,"budget",budget,"founders",
              sum(x["count"] for x in original["starterFauna"]),"->",
              sum(x["count"] for x in initial))
    print("FAUNA_CALIBRATION_OK",("verified" if check else "applied"),
          len(base["species"]),"species",len(base["presets"]),"presets")

if __name__=="__main__":
    a=argparse.ArgumentParser()
    x=a.add_mutually_exclusive_group(required=True)
    x.add_argument("--apply",action="store_true")
    x.add_argument("--check",action="store_true")
    args=a.parse_args()
    run(args.check)
