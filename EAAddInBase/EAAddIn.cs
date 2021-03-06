﻿using EAAddInBase.DataAccess;
using EAAddInBase.MDGBuilder;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using EAAddInBase.Utils;

namespace EAAddInBase
{
    public abstract class EAAddIn
    {
        private readonly Atom<EA.Repository> eaRepository = new LoggedAtom<EA.Repository>("ea.repository", null);

        private readonly MenuHandler menuHandler;

        private readonly Atom<Option<ModelEntity>> contextItem =
            new LoggedAtom<Option<ModelEntity>>("ea.contextItem", Options.None<ModelEntity>());

        private readonly Atom<Option<ContextItemHandler>> contextItemHandler =
            new LoggedAtom<Option<ContextItemHandler>>("ea.contextItemHandler", Options.None<ContextItemHandler>());

        private readonly Atom<Option<MDGTechnology>> technology =
            new LoggedAtom<Option<MDGTechnology>>("ea.addIn.technology", Options.None<MDGTechnology>());

        private readonly Atom<IEntityWrapper> entityWrapper =
            new LoggedAtom<IEntityWrapper>("ea.addIn.entityWrapper", new EntityWrapper());

        private readonly Atom<Option<ValidationHandler>> validationHandler =
            new LoggedAtom<Option<ValidationHandler>>("ea.addIn.validationHandler", Options.None<ValidationHandler>());

        public EAAddIn()
        {
            menuHandler = new MenuHandler(contextItem);
        }

        public abstract String AddInName { get; }

        public abstract Tuple<Option<IEntityWrapper>, IEnumerable<ValidationRule>> Bootstrap(IReadableAtom<EA.Repository> repository);

        public virtual Option<MDGTechnology> BootstrapTechnology()
        {
            return Options.None<MDGTechnology>();
        }

        #region component registration
        protected void Register(IMenuItem menu)
        {
            menuHandler.Register(menu);
        }
        #endregion

        #region manage state
        private void RepositoryChanged(EA.Repository repository)
        {
            eaRepository.Exchange(repository, GetType());
        }
        #endregion

        #region add in actions
        public string EA_Connect(EA.Repository repository)
        {
            RepositoryChanged(repository);

            var data = Bootstrap(eaRepository);

            data.Item1.Do(wrapper =>
            {
                entityWrapper.Exchange(wrapper, GetType());
            });

            validationHandler.Exchange(Options.Some(new ValidationHandler(eaRepository, data.Item2)), GetType());

            var ciHandler = new ContextItemHandler(contextItem, eaRepository, entityWrapper);
            contextItemHandler.Exchange(Options.Some(ciHandler), GetType());

            return "";
        }

        public void EA_Disconnect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public object EA_OnInitializeTechnologies(EA.Repository repository)
        {
            technology.Exchange(BootstrapTechnology(), GetType());
            return technology.Val.Select(tech =>
                {
                    System.Diagnostics.Debug.WriteLine("Technology ID: {0}, XML: {1}", tech.ID, tech.ToXml().ToString());
                    return tech.ToXml().ToString();
                }).GetOrElse("");
        }

        public String EA_OnRetrieveModelTemplate(EA.Repository repository, String location)
        {
            return (from tech in technology.Val
                    from template in tech.ModelTemplates
                    where template.ResourceName == location
                    select template.GetXmi())
                   .FirstOption()
                   .GetOrElse("");
        }
        #endregion

        #region menu actions
        public object EA_GetMenuItems(EA.Repository repository, String menuLocation, String menuName)
        {
            RepositoryChanged(repository);
            contextItemHandler.Val.Do(cih => cih.MenuLocationChanged(menuLocation));

            var itemNames = menuHandler.GetMenuItems(menuName);

            return itemNames;
        }

        public void EA_GetMenuState(EA.Repository repository, string menuLocation, string menuName, string itemName, ref bool isEnabled, ref bool isChecked)
        {
            RepositoryChanged(repository);
            contextItemHandler.Val.Do(cih => cih.MenuLocationChanged(menuLocation));

            var enabled = menuHandler.IsItemEnabled(menuName, itemName);

            isEnabled = enabled;
        }

        public void EA_MenuClick(EA.Repository repository, string menuLocation, string menuName, string itemName)
        {
            RepositoryChanged(repository);
            contextItemHandler.Val.Do(cih => cih.MenuLocationChanged(menuLocation));

            menuHandler.HandleClick(menuName, itemName);
        }
        #endregion

        #region element actions

        public readonly EventManager<ModelEntity, EntityModified> OnEntityCreated =
            new EventManager<ModelEntity, EntityModified>(
                EntityModified.NotModified,
                (acc, v) => acc == EntityModified.Modified ? acc : v);

        public readonly EventManager<ModelEntity, object> OnEntityModified =
            new EventManager<ModelEntity, object>(
                Unit.Instance,
                (acc, _) => acc);

        public readonly EventManager<ModelEntity, DeleteEntity> OnDeleteEntity =
            new EventManager<ModelEntity, DeleteEntity>(
                DeleteEntity.Delete,
                (acc, v) => acc == DeleteEntity.PreventDelete ? acc : v);

        private R Handle<R>(EventManager<ModelEntity, R> em, Func<dynamic> getEaObject)
        {
            return em.Handle(() => entityWrapper.Val.Wrap(getEaObject()));
        }

        public bool EA_OnPreNewElement(EA.Repository repository, EA.EventProperties info)
        {
            RepositoryChanged(repository);

            return true;
        }

        /// <summary>
        /// EA_OnPostNewElement notifies Add-Ins that a new element has been created on a diagram. It enables Add-Ins to
        /// modify the element upon creation.
        /// 
        /// This event occurs after a user has dragged a new element from the Toolbox or Resources window onto a diagram.
        /// The notification is provided immediately after the element is added to the model. Set Repository.SuppressEADialogs
        /// to true to suppress Enterprise Architect from showing its default dialogs.
        /// </summary>
        /// <returns>Return True if the element has been updated during this notification. Return False otherwise.</returns>
        public bool EA_OnPostNewElement(EA.Repository repository, EA.EventProperties info)
        {
            RepositoryChanged(repository);

            var elementId = info.ExtractElementId();

            var entityModified = Handle(OnEntityCreated, () => eaRepository.Val.GetElementByID(elementId));

            return entityModified.AsBool;
        }

        public bool EA_OnPreDeleteElement(EA.Repository repository, EA.EventProperties info)
        {
            RepositoryChanged(repository);

            var elementId = info.ExtractElementId();

            var deleteElement = Handle(OnDeleteEntity, () => eaRepository.Val.GetElementByID(elementId));

            return deleteElement.AsBool;
        }

        public bool EA_OnPostNewConnector(EA.Repository repository, EA.EventProperties info)
        {
            RepositoryChanged(repository);

            var connectorId = info.ExtractConnectorId();

            var entityModified = Handle(OnEntityCreated, () => eaRepository.Val.GetConnectorByID(connectorId));

            return entityModified.AsBool;
        }

        public bool EA_OnPreDeleteConnector(EA.Repository repository, EA.EventProperties info)
        {
            RepositoryChanged(repository);

            var connectorId = info.ExtractConnectorId();

            var deleteConnector = Handle(OnDeleteEntity, () => eaRepository.Val.GetConnectorByID(connectorId));

            return deleteConnector.AsBool;
        }

        public void EA_OnNotifyContextItemModified(EA.Repository repository, string guid, EA.ObjectType ot)
        {
            RepositoryChanged(repository);

            if (ot == EA.ObjectType.otElement)
            {
                Handle(OnEntityModified, () => eaRepository.Val.GetElementByGuid(guid));
            }
        }

        public void EA_OnContextItemChanged(EA.Repository repository, string guid, EA.ObjectType ot)
        {
            RepositoryChanged(repository);

            contextItemHandler.Val.Do(cih => cih.ContextItemChanged(guid, ot));
        }

        /// <summary>
        /// EA_OnContextItemDoubleClicked notifies Add-Ins that the user has double-clicked the item currently in context.
        /// 
        /// This event occurs when a user has double-clicked (or pressed ( Enter ) ) on the item in context, either in a diagram
        /// or in the Project Browser. Add-Ins to handle events can subscribe to this broadcast function.
        /// </summary>
        /// <returns>Return True to notify Enterprise Architect that the double-click event has been handled by an Add-In.
        /// Return False to enable Enterprise Architect to continue processing the event.</returns>
        public bool EA_OnContextItemDoubleClicked(EA.Repository repository, string guid, EA.ObjectType ot)
        {
            RepositoryChanged(repository);

            if (ot == EA.ObjectType.otElement)
            {
                //return customDetailViewHandler.CallElementDetailViews(() => eaRepository.Val.GetElementByGuid(guid)).Val;
            }

            return false;
        }
        #endregion

        #region validation
        public void EA_OnInitializeUserRules(EA.Repository repository)
        {
            RepositoryChanged(repository);

            validationHandler.Val.Do(handler =>
            {
                handler.RegisterRules();
            });
        }

        public void EA_OnStartValidation(EA.Repository repository, object args)
        {
            RepositoryChanged(repository);

            validationHandler.Val.Do(handler =>
            {
                handler.PrepareRules(args as string[]);
            });
        }

        public void EA_OnEndValidation(EA.Repository repository, object args)
        {
            RepositoryChanged(repository);

            validationHandler.Val.Do(handler =>
            {
                handler.CleanUpRules(args as string[]);
            });
        }

        public void EA_OnRunElementRule(EA.Repository repository, string ruleID, EA.Element element)
        {
            RepositoryChanged(repository);

            RunRule(ruleID, () =>
            {
                return entityWrapper.Val.Wrap(element);
            });
        }

        public void EA_OnRunPackageRule(EA.Repository repository, string ruleID, long packageID)
        {
            RepositoryChanged(repository);

            RunRule(ruleID, () =>
            {
                return entityWrapper.Val.Wrap(repository.GetPackageByID((int)packageID));
            });
        }

        public void EA_OnRunDiagramRule(EA.Repository repository, string ruleID, long diagramID)
        {
            RepositoryChanged(repository);

            RunRule(ruleID, () =>
            {
                return entityWrapper.Val.Wrap(repository.GetDiagramByID((int)diagramID));
            });
        }

        public void EA_OnRunConnectorRule(EA.Repository repository, string ruleID, long connectorID)
        {
            RepositoryChanged(repository);

            RunRule(ruleID, () =>
            {
                return entityWrapper.Val.Wrap(repository.GetConnectorByID((int)connectorID));
            });
        }

        private void RunRule(String ruleId, Func<ModelEntity> getEntity)
        {
            validationHandler.Val.Do(handler =>
            {
                handler.ExecuteRule(ruleId, getEntity);
            });
        }
        #endregion
    }

    public class EntityModified
    {
        public static readonly EntityModified Modified = new EntityModified(true);
        public static readonly EntityModified NotModified = new EntityModified(false);

        private EntityModified(bool val) { AsBool = val; }

        public bool AsBool { get; private set; }
    }

    public class DeleteEntity
    {
        public static readonly DeleteEntity Delete = new DeleteEntity(true);
        public static readonly DeleteEntity PreventDelete = new DeleteEntity(false);

        private DeleteEntity(bool val) { AsBool = val; }

        public bool AsBool { get; private set; }
    }
}
