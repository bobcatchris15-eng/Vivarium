import argparse, importlib.util, json, os, subprocess, tempfile, unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('lab',Path(__file__).with_name('visual_lab.py')); lab=importlib.util.module_from_spec(spec); spec.loader.exec_module(lab)
class LabTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory(); self.repo=Path(self.temp.name); (self.repo/'game').mkdir(); (self.repo/'game'/'asset.tres').write_text('asset'); subprocess.run(['git','init',str(self.repo)],capture_output=True,check=True); subprocess.run(['git','-C',str(self.repo),'add','.'],check=True); subprocess.run(['git','-C',str(self.repo),'-c','user.name=Test','-c','user.email=test@example.invalid','commit','-m','fixture'],capture_output=True,check=True); self.act('initialize'); self.exp=self.repo/'build/visual-lab/experiments/test'
 def tearDown(self): self.temp.cleanup()
 def act(self,c,**kw): return lab.execute(argparse.Namespace(repo=str(self.repo),experiment='test',command=c,decision=kw.get('decision'),side=kw.get('side'),author_identity=kw.get('author_identity')))
 def test_resume_concrete_baseline(self):
  result=self.act('resume'); self.assertTrue(result['performed']); self.assertEqual(self.act('status')['stage'],'before'); self.assertEqual((self.exp/'baseline/source/game/asset.tres').read_text(),'asset'); self.assertEqual(self.act('resume')['next_action']['kind'],'capture-needed')
 def test_traversal(self):
  with self.assertRaises(ValueError): lab.confined(self.repo,self.repo/'../escape')
 def test_reparse(self):
  link=self.repo/'junction'; dest=self.repo/'game'
  if os.name=='nt': subprocess.run(['cmd','/c','mklink','/J',str(link),str(dest)],capture_output=True,check=True)
  else: link.symlink_to(dest,target_is_directory=True)
  try:
   with self.assertRaises(ValueError): lab.confined(self.repo,link/'asset.tres')
  finally:
   if os.name=='nt': os.rmdir(link)
   else: link.unlink()
 def test_halt(self):
  self.act('halt')
  with self.assertRaises(ValueError): self.act('resume')
  self.assertTrue(self.act('status')['halted']); self.act('unhalt'); self.assertTrue(self.act('resume')['performed']); (self.repo/'.clanker').mkdir(); (self.repo/'.clanker/HALT').touch()
  with self.assertRaises(ValueError): self.act('blind')
 def test_atomic(self):
  p=self.repo/'atomic.json'; lab.atomic(p,{'v':1}); lab.atomic(p,{'v':2}); self.assertEqual(lab.read(p),{'v':2}); self.assertFalse(list(self.repo.glob('*.tmp')))
 def test_missing_evidence_rejected(self):
  self.act('baseline')
  with self.assertRaises(FileNotFoundError): self.act('decision',decision='accept')
  self.assertFalse((self.exp/'decision.json').exists())
 def test_baseline_tamper_rejected(self):
  self.act('baseline'); (self.exp/'baseline/source/game/asset.tres').write_text('changed')
  with self.assertRaises(ValueError): lab.verify_baseline(self.exp)
 def test_independent_critique_defects(self):
  self.act('baseline'); self.act('authored',author_identity='author'); (self.exp/'critic').mkdir(); lab.atomic(self.exp/'critic/critique.json',{'critic_identity':'author'})
  with self.assertRaisesRegex(ValueError,'independent'): self.act('decision',decision='accept')
  lab.atomic(self.exp/'critic/critique.json',{'critic_identity':'critic','observed_defects':['one']})
  with self.assertRaisesRegex(ValueError,'three'): self.act('decision',decision='accept')
 def test_capture_plan_halt_and_baseline(self):
  self.act('baseline'); plan=self.act('capture-plan',side='before'); self.assertTrue(plan['baseline_manifest_sha256']); (self.repo/'game/asset.tres').write_text('changed')
  with self.assertRaisesRegex(ValueError,'differs'): self.act('capture-plan',side='before')
 def test_fixture_gallery_requires_matched_successful_captures(self):
  for side in ('before','after'):
   directory=self.exp/'captures'/side; directory.mkdir(parents=True)
   lab.atomic(directory/'launch.json',{'mode':'material-port-review','actual_render':True,'exit_code':0})
   (directory/'scene #1.png').write_bytes(b'fixture')
  result=lab.compare_gallery(self.exp)
  self.assertEqual(result['kind'],'static-fixture-gallery')
  gallery=lab.read(self.exp/'compare/gallery.json'); self.assertFalse(gallery['performance_evaluated']); self.assertIsNone(gallery['aesthetic_verdict'])
  self.assertIn('scene%20%231.png',(self.exp/'compare/index.html').read_text())
  (self.exp/'captures/after/scene #1.png').rename(self.exp/'captures/after/other.png')
  with self.assertRaisesRegex(ValueError,'matched'): lab.compare_gallery(self.exp)
  lab.atomic(self.exp/'captures/after/launch.json',{'mode':'reference','actual_render':True,'exit_code':0})
  with self.assertRaisesRegex(ValueError,'material'): lab.compare_gallery(self.exp)
 def test_exclusive_filestream(self):
  lease=self.repo/'lease'; command="$f=[IO.File]::Open($args[0],[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); try { try { $g=[IO.File]::Open($args[0],[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $g.Dispose(); exit 2 } catch [IO.IOException] { Write-Output 'LEASE_EXCLUSIVE_OK' } } finally { $f.Dispose() }"
  # Pass literal path by embedding only a generated, single-quote escaped temporary path.
  command=command.replace('$args[0]',"'"+str(lease).replace("'","''")+"'")
  r=subprocess.run(['pwsh','-NoProfile','-Command',command],capture_output=True,text=True); self.assertEqual(r.returncode,0,r.stderr); self.assertIn('LEASE_EXCLUSIVE_OK',r.stdout)
if __name__=='__main__': unittest.main(verbosity=2)
