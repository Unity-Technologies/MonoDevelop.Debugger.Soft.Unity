rm -rf build-mpack
rm -rf build

xbuild /p:OutputPath=build /p:Configuration=Release /t:Rebuild

mkdir build-mpack

cd build-mpack

mono "../../monodevelop/main/build/bin/mdtool.exe" setup pack ../build/MonoDevelop.Debugger.Soft.Unity.dll ../UnityUtilities/build/UnityUtilities.dll

cd ..