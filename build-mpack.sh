rm -rf build-mpack
mkdir build-mpack
xbuild /p:Configuration=Release /t:Rebuild

cd build-mpack

mono "../../monodevelop/main/build/bin/mdtool.exe" setup pack ../obj/Release/MonoDevelop.Debugger.Soft.Unity.dll ../obj/Release/UnityUtilities.dll

cd ..