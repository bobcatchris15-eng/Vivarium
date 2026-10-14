"""Visual lab durable state/evidence helper; standard library only. PS owns GPU leases."""
import argparse, hashlib, html, importlib.util, json, os, re, secrets, shutil, subprocess, sys
from urllib.parse import quote
from datetime import datetime, timezone
from pathlib import Path

def stamp(): return datetime.now(timezone.utc).isoformat()
def digest(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def confined(root, path):
    root, path = Path(root).absolute(), Path(path).absolute()
    for component in (path, *path.parents):
        if component.exists() and (component.is_symlink() or (getattr(component.lstat(), 'st_file_attributes', 0) & 0x400)):
            raise ValueError('reparse/symlink path forbidden: '+str(component))
    root, path = root.resolve(), path.resolve()
    if path != root and root not in path.parents: raise ValueError('path escapes allowed root: '+str(path))
    return path
def atomic(path, obj):
    path=Path(path); path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_name(path.name+'.'+secrets.token_hex(8)+'.tmp')
    try:
        with temp.open('x',encoding='utf-8') as f:
            json.dump(obj,f,indent=2); f.flush(); os.fsync(f.fileno())
        os.replace(temp,path)
    finally:
        if temp.exists(): temp.unlink()
def read(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def git(repo,*args): return subprocess.check_output(['git','-C',str(repo),*args]).decode('utf-8').strip()
def source_paths(repo):
    raw=subprocess.check_output(['git','-C',str(repo),'ls-files','-z','--cached','--others','--exclude-standard','--','game','src','config'])
    paths=[]
    for rel in sorted(set(raw.decode('utf-8').split('\0'))- {''}):
        parts=Path(rel).parts
        if any(x.lower() in ('artifacts','build','.godot','tools','bin','obj') for x in parts): continue
        if re.search(r'(^|[/\\.])(env|credentials?|secrets?|tokens?|private[-_]?key)([/\\.]|$)',rel,re.I): continue
        if Path(rel).suffix.lower() in ('.pem','.pfx','.p12','.key'): continue
        p=confined(repo,repo/rel)
        if p.is_file(): paths.append((rel,p))
    return paths
def baseline(repo, exp):
    target=exp/'baseline'
    if target.exists(): raise ValueError('protected baseline already exists')
    target.mkdir()
    try:
        status=git(repo,'status','--porcelain=v1','--untracked-files=all')
        records=[]
        for rel,p in source_paths(repo):
            dest=confined(target,target/'source'/rel); dest.parent.mkdir(parents=True,exist_ok=True)
            before=digest(p); shutil.copyfile(p,dest)
            if digest(p)!=before or digest(dest)!=before: raise ValueError('source changed during snapshot: '+rel)
            records.append({'path':rel,'sha256':before,'bytes':p.stat().st_size})
        manifest={'schema_version':1,'created_utc':stamp(),'revision':git(repo,'rev-parse','HEAD'),'git_status':status,'files':records,'policy':'all eligible tracked and untracked source copied; credentials/build/cache excluded'}
        atomic(target/'manifest.json',manifest)
        (target/'manifest.sha256').write_text(digest(target/'manifest.json'),encoding='ascii')
    except Exception:
        # Retain interrupted evidence; never silently retry over it.
        atomic(target/'incomplete.json',{'at':stamp(),'error':'snapshot incomplete; reconcile before retry'})
        raise
    return manifest
def verify_baseline(exp):
    root=exp/'baseline'; m=root/'manifest.json'
    if (root/'incomplete.json').exists() or digest(m)!=(root/'manifest.sha256').read_text(): raise ValueError('baseline manifest integrity failure')
    obj=read(m)
    for record in obj['files']:
        if digest(confined(root,root/'source'/record['path']))!=record['sha256']: raise ValueError('baseline source integrity failure')
    return obj
def clean_png(src,dest):
    # Remove ancillary metadata; retain image rendering chunks, including transparency.
    data=src.read_bytes()
    if data[:8]!=b'\x89PNG\r\n\x1a\n': raise ValueError('not a PNG: '+str(src))
    out=data[:8]; pos=8
    while pos<len(data):
        n=int.from_bytes(data[pos:pos+4],'big'); chunk=data[pos+4:pos+8]; end=pos+12+n
        if end>len(data): raise ValueError('truncated PNG')
        if chunk in (b'IHDR',b'PLTE',b'IDAT',b'IEND',b'tRNS'): out+=data[pos:end]
        pos=end
    dest.write_bytes(out)
def blind(exp):
    before,after=exp/'captures'/'before',exp/'captures'/'after'
    a={p.relative_to(before).as_posix():p for p in before.rglob('*.png')}; b={p.relative_to(after).as_posix():p for p in after.rglob('*.png')}
    if not a or a.keys()!=b.keys(): raise ValueError('paired images missing or scene names differ')
    critic=exp/'critic'; private=exp/'private'
    if critic.exists() or private.exists(): raise ValueError('blind evidence already exists')
    critic.mkdir(); private.mkdir(); mapping=[]; cards=[]
    names=list(a); secrets.SystemRandom().shuffle(names)
    for i,name in enumerate(names,1):
        swap=bool(secrets.randbits(1)); files=[b[name],a[name]] if swap else [a[name],b[name]]
        for side,src in zip(('A','B'),files):
            dest=critic/f'pair-{i:03d}-{side}.png'; clean_png(confined(exp,src),dest)
        mapping.append({'pair':i,'A':'after' if swap else 'before','B':'before' if swap else 'after','scene':name,'before_sha256':digest(a[name]),'after_sha256':digest(b[name])})
        cards.append(f'<section><h2>Pair {i}</h2><div><figure><img src="pair-{i:03d}-A.png"><figcaption>A</figcaption></figure><figure><img src="pair-{i:03d}-B.png"><figcaption>B</figcaption></figure></div></section>')
    (critic/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Blind visual review</title><style>div{display:flex}figure{width:50%;margin:4px}img{width:100%}</style>'+''.join(cards),encoding='utf-8')
    atomic(private/'mapping.json',mapping)
    atomic(critic/'manifest.json',{'created_utc':stamp(),'pairs':len(names),'images':[{'file':p.name,'sha256':digest(p)} for p in sorted(critic.glob('*.png'))]})
    return {'critic':str(critic),'manifest_sha256':digest(critic/'manifest.json')}
def check_halt(repo,exp):
    if (repo/'.clanker'/'HALT').exists() or (exp/'HALT').exists(): raise ValueError('HALT active')

def compare_gallery(exp):
    """PNG fixture comparison only; never substitutes for reference/performance gates."""
    sides=[exp/'captures'/side for side in ('before','after')]
    for side in sides:
        launch=read(side/'launch.json')
        if launch.get('mode') not in ('material-port-review','world-review') or launch.get('actual_render') is not True or launch.get('exit_code')!=0:
            raise ValueError('successful material/world fixture captures required for PNG gallery')
    if read(sides[0]/'launch.json')['mode'] != read(sides[1]/'launch.json')['mode']:
        raise ValueError('matched capture modes required')
    maps=[{p.relative_to(side).as_posix():p for p in side.rglob('*.png')} for side in sides]
    if not maps[0] or maps[0].keys()!=maps[1].keys(): raise ValueError('matched PNG scene names required')
    out=confined(exp,exp/'compare'); out.mkdir(exist_ok=True)
    cards=[]; records=[]
    for name in sorted(maps[0]):
        images=[]; record={'scene':name}
        for side,paths in zip(('before','after'),maps):
            p=confined(exp,paths[name]); record[side+'_sha256']=digest(p)
            uri=quote(os.path.relpath(p,out).replace('\\','/'),safe='/')
            images.append(f'<figure><img loading="lazy" src="{uri}"><figcaption>{side}</figcaption></figure>')
        cards.append('<section><h2>'+html.escape(name)+'</h2><div>'+''.join(images)+'</div></section>'); records.append(record)
    report={'kind':'static-fixture-gallery','created_utc':stamp(),'performance_evaluated':False,'aesthetic_verdict':None,'images':records}
    atomic(out/'gallery.json',report)
    (out/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Matched fixture gallery</title><style>body{font:16px system-ui}div{display:flex}figure{width:50%;margin:4px}img{width:100%}</style><h1>Matched fixture gallery</h1><p>Diagnostic still comparison. No performance or aesthetic acceptance is implied. Use the separate blinded evidence for independent criticism.</p>'+''.join(cards),encoding='utf-8')
    return {'report':str(out/'index.html'),'kind':report['kind']}
def record_decision(repo,exp,decision):
    verify_baseline(exp)
    if decision=='accept':
        critique=read(confined(exp,exp/'critic'/'critique.json')); m=confined(exp,exp/'critic'/'manifest.json')
        state=read(repo/'docs'/'visual-lab'/(exp.name+'.json'))
        if not critique.get('critic_identity') or critique['critic_identity']==state.get('author_identity') or not state.get('author_identity'):
            raise ValueError('independent critic and recorded author identities required')
        defects=critique.get('observed_defects',[])
        if len(defects)<3 or any(not isinstance(d,str) or not d.strip() for d in defects):
            raise ValueError('three observed defects required')
        if critique.get('blind_manifest_sha256')!=digest(m) or critique.get('verdict')!='accept' or critique.get('motion_reviewed') is not True: raise ValueError('critique does not authorize acceptance')
        for item in read(m)['images']:
            if digest(confined(exp/'critic',exp/'critic'/item['file']))!=item['sha256']: raise ValueError('blind image changed')
        motion=confined(exp,exp/critique['motion_file'])
        if motion.stat().st_size==0 or digest(motion)!=critique.get('motion_sha256'): raise ValueError('motion evidence missing or changed')
        if motion.suffix.lower() not in ('.mp4','.webm','.gif','.mkv','.avi'): raise ValueError('motion artifact has unsupported format')
        for side in ('before','after'):
            cap=exp/'captures'/side; launch=read(cap/'launch.json')
            if launch.get('exit_code')!=0 or launch.get('actual_render') is not True or not list(cap.rglob('*.png')): raise ValueError('actual render evidence missing')
            if side=='before' and launch.get('baseline_manifest_sha256')!=digest(exp/'baseline'/'manifest.json'): raise ValueError('capture baseline authentication missing')
        report_path=confined(exp,exp/'performance'/'render_report.json')
        spec=importlib.util.spec_from_file_location('report_validator',repo/'scripts'/'visuals'/'validate-report.py'); module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        report=read(report_path); errors=module.validate(report,report_path.parent)
        if errors: raise ValueError('performance rejected: '+'; '.join(errors))
        if report['provenance']['source_tree_sha256']!=read(exp/'captures'/'after'/'launch.json').get('source_tree_sha256'): raise ValueError('performance source differs from candidate capture')
    result={'decision':decision,'recorded_utc':stamp(),'critique_sha256':digest(exp/'critic'/'critique.json') if (exp/'critic'/'critique.json').exists() else None}
    atomic(exp/'decision.json',result); return result
def execute(args):
    repo=Path(args.repo).resolve(); state_root=confined(repo,repo/'docs'/'visual-lab'); evidence_root=confined(repo,repo/'build'/'visual-lab'/'experiments')
    if args.command=='inventory':
        return {'experiments':[p.stem for p in state_root.glob('*.json')],'halted':(repo/'.clanker'/'HALT').exists(),'capture_modes':['reference','material-port-review','world-review'],'scheduler':'agent heartbeat consumes next_action; no paid API calls'}
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}',args.experiment or ''): raise ValueError('safe experiment ID required')
    exp=confined(evidence_root,evidence_root/args.experiment); statepath=confined(state_root,state_root/(args.experiment+'.json'))
    if args.command=='initialize':
        check_halt(repo,exp)
        if statepath.exists() or exp.exists(): raise ValueError('experiment exists')
        exp.mkdir(parents=True); state={'id':args.experiment,'created_utc':stamp(),'stage':'baseline','evidence':str(exp),'history':[]}; atomic(statepath,state); return state
    state=read(statepath)
    if args.command=='halt': confined(exp,exp/'HALT').touch(); return {'halted':True}
    if args.command=='unhalt':
        confined(exp,exp/'HALT').unlink(missing_ok=True); return {'halted':False,'global_halt':(repo/'.clanker'/'HALT').exists()}
    if args.command=='status': return dict(state,halted=(exp/'HALT').exists() or (repo/'.clanker'/'HALT').exists())
    check_halt(repo,exp)
    command=args.command
    if command=='resume':
        command={'baseline':'baseline','before':'capture-needed','author':'author-needed','after':'capture-needed','blind':'blind','critique':'critique-needed','compare':'compare','decision':'decision-needed','complete':'complete'}.get(state['stage'],'reconcile')
    result={}
    if command=='baseline':
        manifest=baseline(repo,exp)
        result={'manifest':str(exp/'baseline'/'manifest.json'),'manifest_sha256':digest(exp/'baseline'/'manifest.json'),'file_count':len(manifest['files']),'revision':manifest['revision']}
        state['stage']='before'
    elif command=='blind': result=blind(exp); state['stage']='critique'
    elif command=='compare':
        out=exp/'compare'; out.mkdir(exist_ok=True)
        sides=[exp/'captures'/side for side in ('before','after')]
        if not any((side/name).exists() for side in sides for name in ('render_report.json','reference_report.json')):
            result=compare_gallery(exp)
        else:
            subprocess.run(['pwsh','-NoProfile','-File',str(repo/'scripts'/'compare-reference.ps1'),'-Before',str(sides[0]),'-After',str(sides[1]),'-Out',str(out)],check=True)
            result={'report':str(out/'index.html'),'kind':'reference-report'}
        state['stage']='decision'
    elif command=='decision': result=record_decision(repo,exp,args.decision); state['stage']='complete'
    elif command=='capture-plan':
        verify_baseline(exp)
        if not args.side: raise ValueError('capture side required')
        cap=confined(exp,exp/'captures'/args.side)
        if cap.exists(): raise ValueError('capture directory already exists')
        records=[{'path':rel,'sha256':digest(p)} for rel,p in source_paths(repo)]
        if args.side=='before' and records != [{'path':r['path'],'sha256':r['sha256']} for r in verify_baseline(exp)['files']]:
            raise ValueError('current source differs from protected baseline; capture before authoring')
        payload='\n'.join(r['path']+' '+r['sha256'].upper() for r in records if Path(r['path']).parts[0] in ('game','src')).encode()
        return {'capture_dir':str(cap),'source_tree_sha256':hashlib.sha256(payload).hexdigest(),'baseline_manifest_sha256':digest(exp/'baseline'/'manifest.json')}
    elif command=='captured':
        side=args.side; launch=read(exp/'captures'/side/'launch.json')
        if launch['exit_code']!=0 or launch.get('actual_render') is not True: raise ValueError('capture failed or actual render missing')
        state['stage']='author' if side=='before' else 'blind'; result={'captured':side}
    elif command=='authored':
        if not args.author_identity: raise ValueError('author identity required')
        state['author_identity']=args.author_identity; state['stage']='after'; result={'candidate_ready':True}
    elif command=='critiqued':
        if not (exp/'critic'/'critique.json').is_file(): raise ValueError('critique file required')
        state['stage']='compare'; result={'critique_received':True}
    elif command.endswith('-needed') or command in ('complete','reconcile'):
        return {'performed':False,'next_action':{'kind':command,'experiment':args.experiment,'stage':state['stage'],'evidence':str(exp)},'reason':'agent or explicit GPU action required; no scheduler called'}
    else: raise ValueError('unknown command')
    state['history'].append({'command':command,'at':stamp()}); state['updated_utc']=stamp(); atomic(statepath,state)
    return {'performed':True,'result':result,'next_action':{'stage':state['stage'],'experiment':args.experiment}}
def main():
    p=argparse.ArgumentParser(); p.add_argument('command'); p.add_argument('--repo',required=True); p.add_argument('--experiment'); p.add_argument('--decision',choices=['accept','reject','revise']); p.add_argument('--side',choices=['before','after']); p.add_argument('--author-identity'); args=p.parse_args()
    try: print(json.dumps(execute(args),indent=2))
    except Exception as exc: print(json.dumps({'ok':False,'error':str(exc)})); return 1
    return 0
if __name__=='__main__': sys.exit(main())
