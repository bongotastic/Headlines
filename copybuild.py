import shutil

shutil.copyfile("bin\Debug\RPStoryteller.dll", "GameData\Headlines\Plugins\RPStoryteller.dll")
# Useful only for bongo on devmachine
shutil.rmtree("C:\KSP dev\RP1play\GameData\Headlines")
shutil.copytree("GameData\Headlines", "C:\KSP dev\RP1play\GameData\Headlines")