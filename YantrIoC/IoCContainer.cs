using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YantrIoC
{
    public class IoCContainer
    {
        public class BindExpression
        {
            internal Type InterfaceType;
            internal Type ConcreteType;
            internal IoCContainer Container;

            internal BindExpression(Type interfaceType, IoCContainer container)
            {
                this.InterfaceType = interfaceType;
                this.Container = container;
            }

            public ToExpression To<TConcrete>()
            {
                Type concrete = typeof(TConcrete);


                Container.Bind(InterfaceType, concrete);
                this.ConcreteType = concrete;
                return new ToExpression(this);
            }
        }

        public class ToExpression
        {
            internal BindExpression BindExpression;
            public ToExpression(BindExpression bindExpression)
            {
                this.BindExpression = bindExpression;
            }

            public ToExpression AsSingleton()
            {
                BindExpression.Container.GoSingleton(this);
                return this;
            }

            public ToExpression Using(Func<object> factoryMethod)
            {
                BindExpression.Container.SetFactory(BindExpression.InterfaceType, factoryMethod);
                return this;
            }
        }

        private readonly Dictionary<Type, Func<object>> providers;

        private readonly Dictionary<Type, object> singletonStorage;


        private readonly List<BindExpression> declairedExpressions;

        private readonly bool strict;

        public IoCContainer(bool strict = false)
        {
            this.strict = strict;

            providers = new Dictionary<Type, Func<object>>();
            singletonStorage = new Dictionary<Type, object>();
            declairedExpressions = new List<BindExpression>();
        }

        public BindExpression Bind<TInterface>(bool overWriteCurrentBinding = false)
        {
            if (overWriteCurrentBinding)
            {
                if (IsBound<TInterface>())
                {
                    Remove<TInterface>();
                }
            }
            var expr = new BindExpression(typeof(TInterface), this);

            declairedExpressions.Add(expr);
            return expr;
        }



        internal void GoSingleton(ToExpression exp)
        {
            if (singletonStorage.ContainsKey(exp.BindExpression.ConcreteType))
            {
                throw new Exception("Type was already bound in singleton scope");
            }
            singletonStorage[exp.BindExpression.InterfaceType] = null;
        }

        private void Bind(Type From, Type To)
        {
            Bind(From, () => Resolve(To));
        }

        private void Bind(Type tFrom, Func<object> factory)
        {
            providers[tFrom] = () => factory();
        }

        private void SetFactory(Type TFrom, Func<object> factory)
        {
            providers[TFrom] = factory;
        }

        public Type GetResolvedType<T>()
        {
            var returnValue = declairedExpressions.FirstOrDefault(bd => bd.InterfaceType == typeof(T));
            return returnValue.ConcreteType;
        }

        public bool IsBound<T>()
        {
            return declairedExpressions.Any(bd => bd.InterfaceType == typeof(T));
        }



        public T Get<T>()
        {
            T returnValue;
            if (singletonStorage.ContainsKey(typeof(T)))
            {
                //Note: If there is a null value stored in the value of the singletonStorage dict, this means that it is not resolved yet, 
                //but should be resolved and saved into the dictionary, so next time, we return the same object,
                //this gives the user the idea that we are using a singleton-like storage for objects
                //
                //if we stored it in "singleton-scope", we should get it out of singleton scope again, otherwise create a new one
                if (singletonStorage[typeof(T)] == null)
                {
                    //resolve it and return, 

                    singletonStorage[typeof(T)] = (T)providers[typeof(T)]();
                }
                returnValue = (T)singletonStorage[typeof(T)];
            }
            else
            {
                returnValue = (T)providers[typeof(T)]();
            }
            if (strict)
            {
                BindExpression expectedExpression = declairedExpressions.FirstOrDefault(exp => exp.InterfaceType == typeof(T));
                if (returnValue.GetType() != expectedExpression.ConcreteType)
                {
                    throw new Exception(String.Format("Expected return of type {0}, got type {1}", expectedExpression.ConcreteType.Name, returnValue.GetType().Name));
                }
            }
            return returnValue;
        }

        [Obsolete("Not yet implimented")]
        public T Get<T>(Dictionary<string, object> namedParameters)
        {
            //this is to come later on
            throw new NotImplementedException();
            if (singletonStorage.ContainsKey(typeof(T)))
            {
                throw new Exception("Cannot use parameters to get a singleton-bound object");
            }
            else
            {
                //now we have asserted that we cannot get a type using named parameters if it has been bound "as singleton, we can now go and resolve the object
                T returnValue = default(T);

                //re-write the "using" func to allow the input of named parameters
                //maybe swtich on the type of factory included with the bindExpression, this way we can use different factories to create different bound resolvings

                return returnValue;
            }
        }


        private object Resolve(Type type)
        {
            var constructor = type.GetConstructors().SingleOrDefault();
            var temp = InvokeConstructor(constructor);

            return temp;
        }


        private object InvokeConstructor(ConstructorInfo ctor)
        {
            var parametersInfo = ctor.GetParameters();
            var parameters = new List<object>();

            foreach (var parameterInfo in parametersInfo)
            {
                parameters.Add(ResolveConstructorParameter(parameterInfo));
            }
            return ctor.Invoke(parameters.ToArray());
        }

        private object ResolveConstructorParameter(ParameterInfo info)
        {
            if (providers.ContainsKey(info.ParameterType))
            {
                return providers[info.ParameterType]();
            }
            else
            {
                //try to invoke the consctructor that has no parameters, not sure if we can start looking for DefaultContructors using attributes or not in 
                // because it could get kinda messy, and we dont want any of that at all.
                var ctor = info.ParameterType.GetConstructors().FirstOrDefault(c => c.GetParameters().Count() == 0);

                //we havnt bound the type, lets see if we can create a new type of the class with no parameters needed
                if (ctor != null)
                {
                    return InvokeConstructor(ctor);
                }
                else
                {
                    var constructor = info.ParameterType.GetConstructors().FirstOrDefault();

                    if (constructor != null)
                    {
                        return InvokeConstructor(constructor);
                    }
                    else
                    {
                        throw new Exception("Failed To Create Dependency : " + info.Name);
                    }
                }
            }
        }

        internal void Remove<T>()
        {
            Type t = typeof(T);
            this.declairedExpressions.RemoveAll(expr => expr.InterfaceType == t);
            this.providers.Remove(t);
            this.singletonStorage.Remove(t);
        }
    }
}
