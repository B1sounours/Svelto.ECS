﻿using System;
using System.Collections.Generic;
using DBC;
using Svelto.ECS.Internal;

#if ENGINE_PROFILER_ENABLED && UNITY_EDITOR
using Svelto.ECS.Profiler;
#endif

namespace Svelto.ECS
{
    public partial class EnginesRoot : IDisposable
    {
        /// <summary>
        /// Dispose an EngineRoot once not used anymore, so that all the
        /// engines are notified with the entities removed.
        /// It's a clean up process.
        /// </summary>
        public void Dispose()
        {
            foreach (var groups in _groupEntityViewsDB)
                foreach (var entityList in groups.Value)
                    entityList.Value.RemoveEntityViewsFromEngines(_entityViewEngines);
        }

        ///--------------------------------------------

        public IEntityFactory GenerateEntityFactory()
        {
            return new GenericEntityFactory(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        public IEntityFunctions GenerateEntityFunctions()
        {
            return new GenericEntityFunctions(new DataStructures.WeakReference<EnginesRoot>(this));
        }

        ///--------------------------------------------

        
        void BuildEntity<T>(EGID entityID, object[] implementors)
            where T : IEntityDescriptor, new()
        {
            EntityFactory.BuildGroupedEntityViews(entityID,
                                                  _groupedEntityViewsToAdd.current,
                                                  EntityDescriptorTemplate<T>.Info,
                                                  implementors);
        }

        void BuildEntity(EGID entityID, EntityDescriptorInfo entityDescriptorInfo,
                                object[] implementors)
        {
            EntityFactory.BuildGroupedEntityViews(entityID,
                                                  _groupedEntityViewsToAdd.current,
                                                  entityDescriptorInfo,
                                                   implementors);
        }

        ///--------------------------------------------

        /// <summary>
        /// This function is experimental and untested. I never used it in production
        /// it may not be necessary.
        /// TODO: understand if this method is useful in a performance critical
        /// scenario
        /// </summary>
        void Preallocate<T>(int groupID, int size) where T : IEntityDescriptor, new()
        {
            var entityViewsToBuild = EntityDescriptorTemplate<T>.Info.entityViewsToBuild;
            var count              = entityViewsToBuild.Length;

            for (var index = 0; index < count; index++)
            {
                var entityViewBuilder = entityViewsToBuild[index];
                var entityViewType    = entityViewBuilder.GetEntityViewType();

                //reserve space for the global pool
                ITypeSafeDictionary dbList;

                //reserve space for the single group
                Dictionary<Type, ITypeSafeDictionary> @group;
                if (_groupEntityViewsDB.TryGetValue(groupID, out group) == false)
                    group = _groupEntityViewsDB[groupID] = new Dictionary<Type, ITypeSafeDictionary>();
                
                if (group.TryGetValue(entityViewType, out dbList) == false)
                    group[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);
                
                if (_groupedEntityViewsToAdd.current.TryGetValue(groupID, out group) == false)
                    group = _groupEntityViewsDB[groupID] = new Dictionary<Type, ITypeSafeDictionary>();
                
                //reserve space to the temporary buffer
                if (group.TryGetValue(entityViewType, out dbList) == false)
                    group[entityViewType] = entityViewBuilder.Preallocate(ref dbList, size);
                else
                    dbList.AddCapacity(size);
            }
        }
        
        ///--------------------------------------------
        /// 
        void RemoveEntity(EGID entityGID)
        {
            var typeSafeDictionary = _groupEntityViewsDB[entityGID.groupID][typeof(EntityInfoView)] as TypeSafeDictionary<EntityInfoView>;
            var entityInfoView = typeSafeDictionary[entityGID.GID];
            var entityViewBuilders = entityInfoView.entityViewsToBuild;
            var entityViewBuildersCount = entityViewBuilders.Length;
            
            //for each entity view generated by the entity descriptor
            for (var i = 0; i < entityViewBuildersCount; i++)
            {
                var entityViewType = entityViewBuilders[i].GetEntityViewType();
                var group = _groupEntityViewsDB[entityGID.groupID];
                
                _groupEntityViewsDB[entityGID.groupID][entityViewType].RemoveEntityFromDicAndEngines(entityGID, _entityViewEngines);
                RemoveEntityViewFromGroup(group, entityViewType, entityGID);
            }
        }

        static void RemoveEntityViewFromGroup(Dictionary<Type, ITypeSafeDictionary> @group, Type entityViewType, EGID id)
        {
            //remove it from entity views group DB
            var typeSafeList = @group[entityViewType];
            if (typeSafeList.Remove(id.GID) == false) //clean up
                @group.Remove(entityViewType);
        }

        void RemoveGroupAndEntitiesFromDB(int groupID)
        {
            foreach (var entiTypeSafeList in _groupEntityViewsDB[groupID])
                entiTypeSafeList.Value.RemoveEntityViewsFromEngines(_entityViewEngines);

            _groupEntityViewsDB.Remove(groupID);
        }

        ///--------------------------------------------

        void SwapEntityGroup(int entityID, int fromGroupID, int toGroupID)
        {
            Check.Require(fromGroupID != toGroupID,
                          "can't move an entity to the same group where it already belongs to");

            var entityegid = new EGID(entityID, fromGroupID);
            var entityViewBuilders =
                ((TypeSafeDictionary<EntityInfoView>) _groupEntityViewsDB[fromGroupID][typeof(EntityInfoView)])
                [entityegid.GID].entityViewsToBuild;
            var entityViewBuildersCount = entityViewBuilders.Length;

            var groupedEntities = _groupEntityViewsDB[fromGroupID];

            Dictionary<Type, ITypeSafeDictionary> groupedEntityViewsTyped;
            if (_groupEntityViewsDB.TryGetValue(toGroupID, out groupedEntityViewsTyped) == false)
            {
                groupedEntityViewsTyped = new Dictionary<Type, ITypeSafeDictionary>();

                _groupEntityViewsDB.Add(toGroupID, groupedEntityViewsTyped);
            }

            for (var i = 0; i < entityViewBuildersCount; i++)
            {
                var entityViewBuilder = entityViewBuilders[i];
                var entityViewType    = entityViewBuilder.GetEntityViewType();

                var           fromSafeList = groupedEntities[entityViewType];
                ITypeSafeDictionary toSafeList;

                if (groupedEntityViewsTyped.TryGetValue(entityViewType, out toSafeList) == false)
                    groupedEntityViewsTyped[entityViewType] = toSafeList = fromSafeList.Create();

                entityViewBuilder.MoveEntityView(entityegid, fromSafeList, toSafeList);
                fromSafeList.Remove(entityegid.GID);
            }
        }

        readonly EntityViewsDB _DB;
        
        //grouped set of entity views, this is the standard way to handle entity views
        readonly Dictionary<int, Dictionary<Type, ITypeSafeDictionary>>         _groupEntityViewsDB;
    }
}