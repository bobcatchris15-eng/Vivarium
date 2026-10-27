import copy
import importlib.util
import unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location('validator', Path(__file__).with_name('validate-report.py'))
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)

def fixture():
    p = {k:'fixture' for k in v.PROVENANCE}; p.update(build_game_sha256='e'*64,source_revision='c'*40,started_utc='2026-10-09T01:40:00Z',startup_kind='cold',display_scaling=1,source_tree_sha256='a'*64, executable_sha256='b'*64, isolation_confirmed=True, ram_bytes=16*1024**3)
    samples = [1000/60]*3600
    windows = [dict(start_s=i,duration_s=1,frames=60,fps=60) for i in range(60)]
    r = dict(invariants=dict(ticks=3,reload_digest='a'*64,replay_expected_digest='b'*64,replay_actual_digest='b'*64),executing_code=dict(game_assembly_sha256='e'*64,sim_assembly_sha256='f'*64,game_assembly_mvid_matches=True,sim_assembly_mvid_matches=True),schema_version=1, provenance=p, world=dict(seed=20260923,tick_before=100,tick_after=100,flora=30,fauna=30,flora_after=30,fauna_after=30,preparation_bio_days=6), scenario=dict(id='frozen',library_version=1,selected_scene='overview',evolves=False,sculpts=False,warmup_seconds=10,duration_seconds=60,scenes=[]), render_settings=dict(width=1600,height=900,scale=1,quality=2,renderer='forward_plus',display='windows',vsync='Disabled',msaa='Msaa4X',screen_aa='Disabled',flora_tiers='existing',flags={},adapter='fixture'), cpu_timing=[dict(elapsed_s=i+1,process_peak_ms=1,simulation_last_ms=0,scope_max_ms={}) for i in range(60)],gpu_timing=None,frame_times=dict(samples_ms=samples,windows=windows,p50_ms=1000/60,p95_ms=1000/60,p99_ms=1000/60,p999_ms=1000/60,worst_ms=1000/60), counters=dict(memory=[dict(elapsed_s=i+1,working_set_bytes=1024**3,vram_bytes=1024**3) for i in range(60)]),digest_before='a'*64,digest_after='a'*64,render_only_digest_before='a'*64,render_only_digest_after='a'*64,artifacts=[dict(name=n,path=n+'.png') for n in ['overview','interaction','ground_vegetation','macro_flora','dense_colony','water_margin','sparse_dry','mixed_depth'] + ['motion_'+str(i).zfill(2) for i in range(12)]],warnings=[])
    r['scenario']['scenes']=r['artifacts']
    return r

class ReportTests(unittest.TestCase):
    def test_measured_production_scale_and_diagnostic_reductions(self):
        r = fixture(); r['render_settings'].update(quality=1, scale=.95)
        self.assertFalse(v.validate(r))
        for scale in (.75, .5, .9, 0, float('nan')):
            r['render_settings']['scale'] = scale
            self.assertIn('visual target resolution/scale mismatch', v.validate(r))
        r['render_settings'].update(quality=2, scale=.95)
        self.assertIn('visual target resolution/scale mismatch', v.validate(r))
        r['render_settings'].update(quality=1, scale=.95, width=1520)
        self.assertIn('visual target resolution/scale mismatch', v.validate(r))

    def test_frame_boundary_windows_use_actual_coverage(self):
        r = fixture()
        r['frame_times']['samples_ms'] = [25.0] * 2400
        windows = [dict(start_s=i*1.025, duration_s=1.025, frames=41, fps=40.0) for i in range(58)]
        r['frame_times']['windows'] = windows
        for key in ('p50_ms','p95_ms','p99_ms','p999_ms','worst_ms'): r['frame_times'][key] = 25.0
        r['counters']['memory'] = [dict(elapsed_s=w['start_s']+w['duration_s'],working_set_bytes=1024**3,vram_bytes=1024**3) for w in windows]
        r['cpu_timing'] = [dict(elapsed_s=w['start_s']+w['duration_s'],process_peak_ms=1,simulation_last_ms=0,scope_max_ms={}) for w in windows]
        self.assertEqual([], v.validate(r))
        missing_memory = copy.deepcopy(r); missing_memory['counters']['memory'].pop()
        self.assertIn('memory series incomplete', v.validate(missing_memory))
        r['frame_times']['windows'] = windows[:-1]
        self.assertTrue(v.validate(r)) # A complete hidden tail still fails coverage.
    def test_population_ready_library(self):
        r=fixture(); r['scenario'].update(library_version=2, flora_population_ready=True)
        self.assertEqual([], v.validate(r))
        r['scenario']['flora_population_ready']=False
        self.assertTrue(v.validate(r))
    def test_viewport_gpu_samples(self):
        r=fixture(); r['viewport_gpu_timing']=dict(available=True,samples_ms=[10,20],mean_ms=15)
        self.assertEqual([],v.validate(r))
        r['viewport_gpu_timing']['samples_ms']=[0,float('nan')]
        self.assertTrue(v.validate(r))
    def test_unavailable_viewport_gpu_is_not_zero_cost(self):
        r=fixture(); r['viewport_gpu_timing']=dict(available=False,samples_ms=[],mean_ms=None)
        self.assertEqual([],v.validate(r))
        r['viewport_gpu_timing']['mean_ms']=0
        self.assertTrue(v.validate(r))
    def test_valid_screening_fixture(self): self.assertEqual([], v.validate(fixture()))
    def test_dropped_frames(self):
        r=fixture(); r['frame_times']['samples_ms']=r['frame_times']['samples_ms'][:-600]; self.assertTrue(v.validate(r))
    def test_low_fps_even_with_good_summary(self):
        r=fixture(); r['frame_times']['windows'][15]['fps']=20; self.assertTrue(v.validate(r))
    def test_memory_growth(self):
        r=fixture()
        for i,m in enumerate(r['counters']['memory']): m['working_set_bytes'] += i*8*1024**2
        self.assertTrue(v.validate(r))
    def test_digest_mismatch(self):
        r=fixture(); r['digest_after']='b'*64; self.assertTrue(v.validate(r))
    def test_live_requires_render_only_invariant(self):
        r=fixture(); r['scenario']['evolves']=True; r['scenario']['id']='live'; r['digest_after']='b'*64; self.assertEqual([],v.validate(r)); r['render_only_digest_after']='b'*64; self.assertTrue(v.validate(r))
    def test_missing_provenance(self):
        r=fixture(); del r['provenance']['driver']; self.assertTrue(v.validate(r))
    def test_nan_timing(self):
        r=fixture(); r['frame_times']['samples_ms'][1]=float('nan'); self.assertTrue(v.validate(r))
    def test_hardware_incompatibility(self):
        a=fixture(); b=copy.deepcopy(a); b['provenance']['gpu']='different'; self.assertTrue(v.compatibility(a,b))
    def test_flags_and_pose_incompatibility(self):
        a=fixture(); b=copy.deepcopy(a); b['render_settings']['flags']={'different':'1'}; self.assertTrue(v.compatibility(a,b))
    def test_missing_images(self): self.assertTrue(v.validate(fixture(),Path(__file__).parent))
    def test_short_measurement(self):
        r=fixture(); r['scenario']['duration_seconds']=10; self.assertTrue(v.validate(r))

    def test_provenance_shape_and_nonhex_hash(self):
        r=fixture(); r['provenance'].update(cpu=False,gpu=42,ram_bytes=-1,source_revision=[],source_tree_sha256='z'*64,started_utc='invalid'); r['cpu_timing']='fabricated'; self.assertTrue(v.validate(r))
    def test_string_boolean_and_id_mode_mismatch(self):
        for value in ('false', True):
            r=fixture(); r['scenario']['evolves']=value; r['digest_after']='b'*64; self.assertTrue(v.validate(r))
    def test_invented_window_counts(self):
        r=fixture(); r['frame_times']['samples_ms']=[25]*2400
        for w in r['frame_times']['windows']: w.update(frames=31,fps=31)
        for k in ('p50_ms','p95_ms','p99_ms','p999_ms','worst_ms'): r['frame_times'][k]=25
        self.assertTrue(v.validate(r))
    def test_duplicate_memory_and_cpu_timestamps(self):
        for section in ('memory','cpu'):
            r=fixture(); series=r['counters']['memory'] if section=='memory' else r['cpu_timing']
            for sample in series: sample['elapsed_s']=0
            self.assertTrue(v.validate(r))
    def test_actual_windows_display_and_module_identity(self):
        r=fixture(); r['render_settings']['display']='Windows'; self.assertFalse(v.validate(r))
        r['executing_code']['game_assembly_mvid_matches']=False; self.assertTrue(v.validate(r))

    def test_missing_display_and_gpu_nan(self):
        r=fixture(); del r['render_settings']['display']; self.assertTrue(v.validate(r))
        r=fixture(); r['gpu_timing']={'draw_ms':float('nan')}; self.assertTrue(v.validate(r))
    def test_duplicate_fake_capture_manifest(self):
        r=fixture(); r['artifacts']=[dict(name='same',path='same.png')]*14; r['scenario']['scenes']=[]; self.assertTrue(v.validate(r))
    def test_live_final_outcome_compatibility(self):
        a=fixture(); b=copy.deepcopy(a); b['world']['tick_after']=999999; b['digest_after']='d'*64; self.assertTrue(v.compatibility(a,b))
    def test_hidden_complete_tail_window(self):
        r=fixture(); r['frame_times']['samples_ms']=[1000/60]*3540+[36]*29; r['scenario']['duration_seconds']=60.044
        r['frame_times']['windows']=r['frame_times']['windows'][:59]; r['counters']['memory']=r['counters']['memory'][:59]; r['cpu_timing']=r['cpu_timing'][:59]
        for key,q in [('p50_ms',.5),('p95_ms',.95),('p99_ms',.99),('p999_ms',.999),('worst_ms',1)]: r['frame_times'][key]=v.percentile(r['frame_times']['samples_ms'],q)
        self.assertTrue(v.validate(r))

if __name__=='__main__': unittest.main()
