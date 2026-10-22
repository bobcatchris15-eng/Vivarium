import json,re
d=json.load(open('E:/Vivarium/build/perf-overhaul/probe1/perf_report.json'))
samples=d['facts']['samples']
draws=[]; prims=[]; backlog=[]; ticks=[]; sim=[]; fps=[]
for s in samples:
    draws.append(int(re.search(r'draws=(\d+)',s).group(1)))
    prims.append(int(re.search(r'prims=(\d+)',s).group(1)))
    backlog.append(int(re.search(r'backlog=\s*(\d+)s',s).group(1)))
    ticks.append(int(re.search(r'ticks/s=\s*(\d+)',s).group(1)))
    sim.append(float(re.search(r'simLast=\s*([\d.]+)ms',s).group(1)))
    fps.append(int(re.search(r'fps=\s*(\d+)',s).group(1)))
print('n samples',len(samples))
print('draws min/max/mean',min(draws),max(draws),round(sum(draws)/len(draws),1))
print('prims min/max/mean',min(prims),max(prims),round(sum(prims)/len(prims)))
print('backlog min/max/mean',min(backlog),max(backlog),round(sum(backlog)/len(backlog),1))
print('ticks/s min/max/mean',min(ticks),max(ticks),round(sum(ticks)/len(ticks),1))
print('simLast mean/max',round(sum(sim)/len(sim),2),max(sim))
print('fps per sec min/max/mean',min(fps),max(fps),round(sum(fps)/len(fps),1))