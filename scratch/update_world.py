with open('src/Vivarium.Sim/World/VivariumWorld.cs', 'r', encoding='utf-8', newline='') as f:
    text = f.read()

target = 'Scheduler.Register("coverage.lichen", Cadence.Flora, 76, Bio(CoverageSystem.StepLichen), phase: 31);'
assert target in text, 'target not found'

nl = '\r\n' if '\r\n' in text else '\n'
replacement = target + nl + '        Scheduler.Register("coverage.plasmodium", Cadence.FaunaMetabolism, 77, Bio(CoverageSystem.StepPlasmodium), phase: 2);'

text = text.replace(target, replacement, 1)

with open('src/Vivarium.Sim/World/VivariumWorld.cs', 'w', encoding='utf-8', newline='') as f:
    f.write(text)

print('Updated VivariumWorld.cs')
