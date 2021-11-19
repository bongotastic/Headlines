import shutil

shutil.copyfile("bin\Debug\Headlines.dll", "GameData\Headlines\Plugins\Headlines.dll")
# Useful only for bongo on devmachine
shutil.rmtree("C:\KSP dev\RP1play\GameData\Headlines")
shutil.copytree("GameData\Headlines", "C:\KSP dev\RP1play\GameData\Headlines")
#shutil.rmtree("C:\KSP dev\RP1\GameData\Headlines")
#shutil.copytree("GameData\Headlines", "C:\KSP dev\RP1\GameData\Headlines")
