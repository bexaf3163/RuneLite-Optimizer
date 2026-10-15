$ErrorActionPreference='Stop'
$cache=Join-Path $PSScriptRoot 'java-build'
$lib=Join-Path $cache 'lib'
New-Item -ItemType Directory -Force $lib | Out-Null
$files=@(
 @('client-1.13.1.jar','https://repo.runelite.net/net/runelite/client/1.13.1/client-1.13.1.jar','5ABED7C968A8EB81C0C2D534E58369C875129EE2C246B03CFBE84CCD891C8DC6'),
 @('runelite-api-1.13.1-runtime.jar','https://repo.runelite.net/net/runelite/runelite-api/1.13.1/runelite-api-1.13.1-runtime.jar','6730AF0FF0DA4976E1D794A834E83E0BF1018CB230F0EA6F979D35257AB1847C'),
 @('guice-4.1.0-no_aop.jar','https://repo.maven.apache.org/maven2/com/google/inject/guice/4.1.0/guice-4.1.0-no_aop.jar','9264C6931C431E928DC64ADC842584D5F57D17B2F3AFF29221F2B3FDEA673DAD'),
 @('javax.inject-1.jar','https://repo.maven.apache.org/maven2/javax/inject/javax.inject/1/javax.inject-1.jar','91C77044A50C481636C32D916FD89C9118A72195390452C81065080F957DE7FF'),
 @('guava-23.2-jre.jar','https://repo.maven.apache.org/maven2/com/google/guava/guava/23.2-jre/guava-23.2-jre.jar','5BE9A7D05BA0CCD74708BC8018AE412255F85843C0B92302E9B9BEFA6ED52564'),
 @('gson-2.8.5.jar','https://repo.maven.apache.org/maven2/com/google/code/gson/gson/2.8.5/gson-2.8.5.jar','233A0149FC365C9F6EDBD683CFE266B19BDC773BE98EABDAF6B3C924B48E7D81'),
 @('slf4j-api-1.7.25.jar','https://repo.maven.apache.org/maven2/org/slf4j/slf4j-api/1.7.25/slf4j-api-1.7.25.jar','18C4A0095D5C1DA6B817592E767BB23D29DD2F560AD74DF75FF3961DBDE25B79')
)
foreach($file in $files){
 $target=Join-Path $lib $file[0]
 if(!(Test-Path $target)){
  $local=Join-Path $env:USERPROFILE ('.runelite\repository2\'+$file[0])
  if(Test-Path $local){Copy-Item -LiteralPath $local -Destination $target}else{Invoke-WebRequest -UseBasicParsing $file[1] -OutFile $target}
 }
 if((Get-FileHash $target).Hash -ne $file[2]){throw ('Companion dependency hash mismatch: '+$file[0])}
}
$ecj=Join-Path $cache 'ecj.jar'
if(!(Test-Path $ecj)){Invoke-WebRequest -UseBasicParsing 'https://repo.maven.apache.org/maven2/org/eclipse/jdt/ecj/3.38.0/ecj-3.38.0.jar' -OutFile $ecj}
if((Get-FileHash $ecj).Hash -ne '97C566B120009C203A2FC8B291F4A9ADBC171CF1CCB70F06F6B4E1828C00CE8E'){throw 'ECJ compiler hash mismatch'}
$javaRuntime=if($env:JAVA_HOME){Join-Path $env:JAVA_HOME 'bin\java.exe'}else{Join-Path $env:LOCALAPPDATA 'RuneLite\jre\bin\java.exe'}
if(!(Test-Path $javaRuntime)){ $javaRuntime=Join-Path $env:USERPROFILE 'AppData\Local\RuneLite\jre\bin\java.exe' }; if(!(Test-Path $javaRuntime)){throw 'Java 17 is required to build the companion (JAVA_HOME or installed RuneLite JRE).'}
$classes=Join-Path $cache ('classes-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $classes | Out-Null
$sources=Get-ChildItem (Join-Path $PSScriptRoot '..\companion') -Filter '*.java' | Select-Object -ExpandProperty FullName
$classPath=((Get-ChildItem $lib -Filter '*.jar').FullName -join ';' )
& $javaRuntime -jar $ecj -11 -proc:none -encoding UTF-8 -cp $classPath -d $classes @sources
if($LASTEXITCODE -ne 0){throw 'Companion compilation failed'}
& $javaRuntime -cp ($classes+';'+$classPath) com.runeliteoptimizer.CompanionTest
if($LASTEXITCODE -ne 0){throw 'Companion safety tests failed'}
$testClasses=Join-Path $cache ('tests-'+[guid]::NewGuid().ToString('N'))
$testPackage=Join-Path $testClasses 'com\runeliteoptimizer'
New-Item -ItemType Directory -Force $testPackage | Out-Null
Move-Item -LiteralPath (Join-Path $classes 'com\runeliteoptimizer\CompanionTest.class') -Destination $testPackage
$meta=Join-Path $classes 'META-INF'
New-Item -ItemType Directory $meta | Out-Null
[IO.File]::WriteAllText((Join-Path $meta 'MANIFEST.MF'),"Manifest-Version: 1.0`r`nPremain-Class: com.runeliteoptimizer.OptimizerAgent`r`n`r`n",[Text.Encoding]::ASCII)
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$jar=Join-Path $cache 'OptimizerTelemetry.jar'
if(Test-Path $jar){Remove-Item -LiteralPath $jar}
# Framework ZipFile.CreateFromDirectory can emit Windows backslashes. JAR lookup
# requires '/' even on Windows, including META-INF/MANIFEST.MF.
$archive=[IO.Compression.ZipFile]::Open($jar,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($item in Get-ChildItem -LiteralPath $classes -File -Recurse){
  $entry=$item.FullName.Substring($classes.Length+1).Replace('\','/')
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$item.FullName,$entry) | Out-Null
 }
} finally {$archive.Dispose()}
& $javaRuntime ('-javaagent:'+$jar) -cp ($testClasses+';'+$jar+';'+$classPath) com.runeliteoptimizer.CompanionTest --agent-smoke
if($LASTEXITCODE -ne 0){throw 'Packaged Java agent startup failed'}
Write-Output 'Independent telemetry companion compiled; no OSRS Path files changed.'
