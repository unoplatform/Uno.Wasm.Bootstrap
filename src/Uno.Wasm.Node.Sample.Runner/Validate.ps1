$testParameters = 'test1; test2'

$appOutput = node app test1 test2

echo "App output: "
echo $appOutput


$result = $appOutput | Select-String -Pattern $testParameters -CaseSensitive -SimpleMatch

 if(!$result){
    throw "Unable to find $($testParameters) in app output";
 }
