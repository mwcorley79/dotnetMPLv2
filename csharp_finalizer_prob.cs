// Mike C.  c# finalizer test.  Muck like destructor in Python, but very different than
// C++ since Python and C# object models are based reference semantics, 
// while C++ is based value semantics  

using System;
using System.Threading;

public class Employee
{
    // Initializing
    public Employee()
    {
        Console.WriteLine("Employee created");
    }

    //deleting (Calling finalizer)
    ~Employee()
    {
        Console.WriteLine("*** Destructor called, Employee deleted ***");
    }


    public static void Main()
    {
       
        Employee emp1 = new Employee();
        Employee emp2 = emp1;

        emp1 = null;  // explicity delete emp1 reference (set to NULL)
        Console.WriteLine("Deleting emp1 explicity...destructor is not called becuase emp2 is reference to emp1");
        
        emp2  = null;  // uncomment to test finalizer
      
        //System.GC.Collect();
        
       // Thread.Sleep(1000);
       
       
        Console.WriteLine(@"Note: if emp2 is deleted excplicity (the above line), you'll see the 
                    destructor announced before this message, otherwsie the referense is deleted 
                    when object then goes out scope... in which case you'll see it announced after this message");

      
        //emp1 is now deleted, because it goes of out scope
    }
}
