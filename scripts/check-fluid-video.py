"""Fail-closed native capture validation; the reference is the owned game viewport."""
import argparse,json,subprocess
from pathlib import Path
from PIL import Image,ImageChops,ImageStat
parser=argparse.ArgumentParser();parser.add_argument("directory",type=Path);args=parser.parse_args();root=args.directory
video=root/"motion-30fps.mp4"
probe=json.loads(subprocess.check_output(["ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=width,height,r_frame_rate,nb_frames","-of","json",str(video)]))["streams"][0]
assert (probe["width"],probe["height"])==(1600,900),"Wrong capture dimensions"
rate=probe["r_frame_rate"].split("/");assert float(rate[0])/float(rate[1])>=29.9,"Capture is not 30 FPS"
assert int(probe["nb_frames"])>=300,"Insufficient recorded motion"
check=root/"video-check.png"
subprocess.run(["ffmpeg","-hide_banner","-loglevel","error","-ss","1","-i",str(video),"-frames:v","1","-y",str(check)],check=True)
reference=Image.open(root/"video-reference.png").convert("RGB");assert reference.size==(1600,900),"Reference viewport size changed"
reference=reference.resize((160,90))
actual=Image.open(check).convert("RGB").resize((160,90))
statistics=ImageStat.Stat(actual);assert max(statistics.stddev)>8,"Capture is blank"
error=sum(ImageStat.Stat(ImageChops.difference(reference,actual)).mean)/3
assert error<20,f"Capture does not match owned viewport: mean error {error:.2f}"
receipt={"dimensions":[1600,900],"fps":30,"frames":int(probe["nb_frames"]),"reference_mean_absolute_error":error,"nonblank":True}
(root/"video-validation.json").write_text(json.dumps(receipt,indent=2),encoding="utf-8")
print(json.dumps(receipt))
