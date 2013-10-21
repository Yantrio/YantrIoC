using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YantrIoC
{
	//todo : move each type of resolution to a factory, and move the bind expressions between each factory, 
	//this will mean we ont have to keep checking on which dictionary/list things belong i. and we ca just go ahead and invoke the corrisponding factory.
	public class IoCContainer
	{
		public class BindExpression
		{
			internal Type InterfaceType;
			internal Type ConcreteType;
			internal IoCContainer Container;
			public bool IsUserDefinedFactory = false;

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
				BindExpression.IsUserDefinedFactory = true;
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

		private void SetFactory(Type from, Func<object> factory)
		{
			providers[from] = factory;
		}

		//this kindof breaks the rules of IoC, and in the future it may be removed for purity sake.
		//but for now, the user may want to get the type and create his own version of it
		//without relying on our constructor
		public Type GetResolvedType<T>()
		{
			var returnValue = declairedExpressions.FirstOrDefault(bd => bd.InterfaceType == typeof(T));
			return returnValue.ConcreteType;
		}

		public bool IsBound<T>()
		{
			return declairedExpressions.Any(bd => bd.InterfaceType == typeof(T));
		}

		public object Get(Type t)
		{

			object returnValue = null;
			if (singletonStorage.ContainsKey(t))
			{
				//Note: If there is a null value stored in the value of the singletonStorage dict, this means that it is not resolved yet, 
				//but should be resolved and saved into the dictionary, so next time, we return the same object,
				//this gives the user the idea that we are using a singleton-like storage for objects
				//
				//if we stored it in "singleton-scope", we should get it out of singleton scope again, otherwise create a new one
				if (singletonStorage[t] == null)
				{
					//resolve it and return, 

					singletonStorage[t] = providers[t]();
				}
				returnValue = singletonStorage[t];
			}
			else
			{
				if (providers.ContainsKey(t))
				{
					returnValue = providers[t]();
				}
				else
				{
					returnValue = Resolve(t);
				}
			}
			if (strict)
			{
				BindExpression expectedExpression = declairedExpressions.FirstOrDefault(exp => exp.InterfaceType == t);
				if (returnValue.GetType() != expectedExpression.ConcreteType)
				{
					throw new Exception(String.Format("Expected return of type {0}, got type {1}", expectedExpression.ConcreteType.Name, returnValue.GetType().Name));
				}
			}
			return returnValue;

		}

		public T Get<T>()
		{
			return (T)Get(typeof(T));
		}

		/// <summary>
		/// Gets using an anonymous type to pass in named constructor paramters.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="anonymousType"></param>
		/// <returns></returns>
		public T Get<T>(object anonymousType)
		{
			return Get<T>(anonymousType.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(anonymousType)));
		}

		public T Get<T>(Dictionary<string, object> namedParameters)
		{
			if (singletonStorage.ContainsKey(typeof(T)))
			{
				throw new Exception("Cannot use parameters to get a singleton-bound object");
			}
			else
			{
				//now we have asserted that we cannot get a type using named parameters if it has been bound "as singleton", we can now go and resolve the object
				T returnValue = default(T);

				//re-write the "using" func to allow the input of named parameters
				//maybe swtich on the type of factory included with the bindExpression, this way we can use different factories to create different bound resolvings

				var declairation = declairedExpressions.FirstOrDefault(exp => exp.InterfaceType == typeof(T));

				if (declairation != null)
				{
					if (declairation.IsUserDefinedFactory)
					{
						throw new Exception("Whoah there nelly, you already are using a custom factory to create this, try binding without the .Using(() => ... ) method if you want to use parameters");
					}
				}


				returnValue = FindBestConstructorAndInvoke<T>(namedParameters);

				return returnValue;
			}
		}

		public T FindBestConstructorAndInvoke<T>(Dictionary<string, object> parameters)
		{

			Type t = typeof(T);
			IEnumerable<ConstructorInfo> orderedConstructors = t.GetConstructors().OrderBy(ctor => ctor.GetParameters().Count()).ToList();

			ConstructorInfo constructorToInvoke = null;

			foreach (var ctor in orderedConstructors)
			{
				//iterate over all the constructors, starting with the constructor with the most arugements, this way we can greedily match the parameters
				if (ctor.GetParameters().All(p => parameters.Any(c => (c.Key == p.Name) && (c.Value.GetType().IsAssignableFrom(p.ParameterType))) || CanResolve(p.ParameterType)))
				{
					constructorToInvoke = ctor;
					break;
				}
			}
			if (constructorToInvoke == null)
			{
				return default(T);
			}

			IList<Object> preparedParameters = new List<object>();

			foreach (var parameter in constructorToInvoke.GetParameters())
			{
				//find the correct parameter and throw it in our collection
				var namedPaaram = parameters.FirstOrDefault(p => p.Key == parameter.Name);
				if (namedPaaram.Value != null)
				{
					preparedParameters.Add(namedPaaram.Value);
				}
				else
				{
					preparedParameters.Add(Get(parameter.ParameterType));
				}
			}

			return (T)constructorToInvoke.Invoke(preparedParameters.ToArray());
		}

		private object Resolve(Type type)
		{
			var constructor = type.GetConstructors().FirstOrDefault();
			var temp = InvokeConstructor(constructor);

			return temp;
		}

		public bool CanResolve<T>()
		{
			return CanResolve(typeof(T));
		}

		private bool CanResolve(Type t)
		{
			if (t.IsAbstract || t.IsInterface)
			{
				return false;
			}
			if (declairedExpressions.Any(be => be.InterfaceType == t || be.ConcreteType == t))
			{
				return true;
			}

			return t.GetConstructors().Any(ctor => CanResolve(ctor));
		}

		private bool CanResolve(ConstructorInfo ctor)
		{
			if (ctor.GetParameters().Count() == 0)
			{
				return true;
			}
			if (ctor.GetParameters().All(p => CanResolve(p.ParameterType)))
			{
				return true;
			}

			return false;
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
				var ctor = info.ParameterType.GetConstructors().FirstOrDefault(c => c.GetParameters().Count() == 0);

				//we havnt bound the type, lets see if we can create a new type of the class with no parameters needed
				if (ctor != null)
				{
					return InvokeConstructor(ctor);
				}
				else
				{
					//else we have bound the type, grab that badboy and invoke it :)
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

		internal void Remove(BindExpression exp)
		{
			declairedExpressions.Remove(exp);
			this.providers.Remove(exp.InterfaceType);
			this.singletonStorage.Remove(exp.InterfaceType);
		}
	}
}
